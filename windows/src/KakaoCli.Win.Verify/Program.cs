using System.Text.Json;
using KakaoCli.Win.Core.Contracts;
using KakaoCli.Win.Core.Monitoring;
using KakaoCli.Win.Data;

var tests = new (string Name, Func<Task> Run)[]
{
    ("invalid configs fail closed", TestInvalidConfigsFailClosed),
    ("cursor store prunes unknown chats", TestCursorStorePrunesUnknownChats),
    ("first run seeds only allowlisted chats", TestFirstRunSeedsAllowlistedChats),
    ("allowlisted message dispatch advances cursor", TestAllowlistedDispatchAdvancesCursor),
    ("unconfigured rows are never materialized", TestUnconfiguredRowsNeverMaterialized),
    ("sink failure does not advance cursor", TestSinkFailureDoesNotAdvanceCursor),
    ("preview truncates deterministically", TestPreviewPolicy),
    ("slack sink validates destination and escapes text", TestSlackSinkSafety),
    ("slack-monitor route avoids broad handlers", TestProgramBoundary),
};

foreach (var test in tests)
{
    await test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

Console.WriteLine("slack-monitor-verification-ok");

static Task TestInvalidConfigsFailClosed()
{
    var invalidConfigs = new[]
    {
        "{}",
        """{"allowlist":[]}""",
        """{"allowlist":[{"label":"missing-id"}]}""",
        """{"allowlist":[{"chat_id":"abc","label":"bad-id"}]}""",
        """{"allowlist":[{"chat_id":123,"label":""}]}""",
        """{"allowlist":[{"chat_id":123,"label":"a"},{"chat_id":123,"label":"b"}]}""",
        """{"allowlist":[{"chat_name":"policy-room","label":"name-only"}]}""",
        """{"allowlist":[{"chat_id":123,"label":"ok","all":true}]}""",
        """{"allowlist":[{"chat_id":123,"label":"ok"}],"all":true}""",
    };

    foreach (var json in invalidConfigs)
    {
        using var document = JsonDocument.Parse(json);
        var result = AllowlistConfig.Validate(document.RootElement);
        Assert(!result.IsValid, $"Expected invalid config: {json}");
        var constructed = false;
        if (result.IsValid && result.Config is not null)
        {
            _ = new FakeReader(result.Config.Allowlist, []);
            constructed = true;
        }
        Assert(!constructed, "Reader must not be constructed for invalid config.");
    }

    return Task.CompletedTask;
}

static async Task TestCursorStorePrunesUnknownChats()
{
    var dir = CreateTempDir();
    var path = Path.Combine(dir, "cursor.json");
    await File.WriteAllTextAsync(path, """
{
  "version": 1,
  "chats": {
    "123": { "last_log_id": 10, "label": "allowed", "updated_at": "2026-04-15T00:00:00Z" },
    "999": { "last_log_id": 99, "label": "denied", "updated_at": "2026-04-15T00:00:00Z" }
  }
}
""");

    var store = new MonitorCursorStore(path);
    var cursors = await store.LoadAsync([new AllowlistedChat(123, "allowed")]);
    Assert(cursors.Count == 1 && cursors[123] == 10, "Cursor load must keep only allowlisted chat.");
    Assert(!cursors.ContainsKey(999), "Cursor load must prune unknown chat id.");
    var rewritten = await File.ReadAllTextAsync(path);
    Assert(!rewritten.Contains("\"999\"", StringComparison.Ordinal), "Cursor load must physically prune unknown chat id.");
}

static async Task TestFirstRunSeedsAllowlistedChats()
{
    var dir = CreateTempDir();
    var allowlist = new[] { new AllowlistedChat(123, "allowed") };
    var reader = new FakeReader(allowlist, [new RawRow(123, 11, () => "history")]);
    var sink = new CapturingSink();
    var poller = new PollingService(allowlist, reader, new MonitorCursorStore(Path.Combine(dir, "cursor.json")), sink, 160);

    var sent = await poller.RunOnceAsync();
    Assert(sent == 0, "First run should seed cursor without forwarding history.");
    Assert(reader.MaxRequested.SetEquals([123]), "Seed must request max log IDs only for allowlisted chats.");
    Assert(sink.Payloads.Count == 0, "First run seed must not send payloads.");
}

static async Task TestAllowlistedDispatchAdvancesCursor()
{
    var dir = CreateTempDir();
    var cursorPath = Path.Combine(dir, "cursor.json");
    var allowlist = new[] { new AllowlistedChat(123, "allowed") };
    await new MonitorCursorStore(cursorPath).SaveAsync(allowlist, new Dictionary<long, long> { [123] = 10 });

    var reader = new FakeReader(allowlist, [new RawRow(123, 11, () => "allowed text")]);
    var sink = new CapturingSink();
    var store = new MonitorCursorStore(cursorPath);
    var poller = new PollingService(allowlist, reader, store, sink, 160);

    var sent = await poller.RunOnceAsync();
    var cursors = await store.LoadAsync(allowlist);
    Assert(sent == 1, "Expected one allowlisted payload.");
    Assert(sink.Payloads.Single().TextPreview == "allowed text", "Payload should include allowlisted preview.");
    Assert(cursors[123] == 11, "Cursor should advance after successful sink send.");
}

static async Task TestUnconfiguredRowsNeverMaterialized()
{
    var dir = CreateTempDir();
    var cursorPath = Path.Combine(dir, "cursor.json");
    var allowlist = new[] { new AllowlistedChat(123, "allowed") };
    await new MonitorCursorStore(cursorPath).SaveAsync(allowlist, new Dictionary<long, long> { [123] = 10 });

    var deniedMaterialized = 0;
    var reader = new FakeReader(
        allowlist,
        [
            new RawRow(999, 999, () =>
            {
                deniedMaterialized++;
                return "must-not-read";
            }),
        ]
    );
    var sink = new CapturingSink();
    var poller = new PollingService(allowlist, reader, new MonitorCursorStore(cursorPath), sink, 160);

    var sent = await poller.RunOnceAsync();
    Assert(sent == 0, "Unconfigured chat must not send payloads.");
    Assert(deniedMaterialized == 0, "Unconfigured row text must never be materialized.");
    Assert(sink.Payloads.Count == 0, "Unconfigured row must not reach sink.");
}

static async Task TestSinkFailureDoesNotAdvanceCursor()
{
    var dir = CreateTempDir();
    var cursorPath = Path.Combine(dir, "cursor.json");
    var allowlist = new[] { new AllowlistedChat(123, "allowed") };
    await new MonitorCursorStore(cursorPath).SaveAsync(allowlist, new Dictionary<long, long> { [123] = 10 });

    var reader = new FakeReader(allowlist, [new RawRow(123, 11, () => "allowed text")]);
    var sink = new CapturingSink(success: false);
    var store = new MonitorCursorStore(cursorPath);
    var poller = new PollingService(allowlist, reader, store, sink, 160);

    var sent = await poller.RunOnceAsync();
    var cursors = await store.LoadAsync(allowlist);
    Assert(sent == 0, "Failed sink sends should not count as sent.");
    Assert(cursors[123] == 10, "Cursor must not advance after sink failure.");
}

static Task TestPreviewPolicy()
{
    Assert(PreviewPolicy.CreatePreview("abcdef", 3) == "abc", "Preview should truncate.");
    Assert(PreviewPolicy.CreatePreview("abc", 10) == "abc", "Short preview should remain intact.");
    Assert(PreviewPolicy.CreatePreview(null, 10) == string.Empty, "Null preview should be empty.");
    return Task.CompletedTask;
}

static async Task TestSlackSinkSafety()
{
    var envName = $"KAKAOCli_TEST_SLACK_{Guid.NewGuid():N}";
    Environment.SetEnvironmentVariable(envName, "http://hooks.slack.com/services/test");
    ExpectThrows<InvalidOperationException>(() => new SlackMessageSink(envName), "HTTP Slack URL should be rejected.");

    Environment.SetEnvironmentVariable(envName, "https://evilslack.com/services/test");
    ExpectThrows<InvalidOperationException>(() => new SlackMessageSink(envName), "Lookalike Slack host should be rejected.");

    var handler = new CapturingHttpHandler();
    Environment.SetEnvironmentVariable(envName, "https://hooks.slack.com/services/test");
    var sink = new SlackMessageSink(envName, new HttpClient(handler));
    var sent = await sink.SendAsync(new MonitorPayload(
        123,
        "policy<&room>",
        11,
        null,
        "sender>",
        "hello <@channel> & text",
        DateTimeOffset.UtcNow.ToString("O"),
        DateTimeOffset.UtcNow.ToString("O"),
        ["https://talk.kakaocdn.net/test-image.png"]
    ));

    Environment.SetEnvironmentVariable(envName, null);
    Assert(sent, "Slack sink should report success for 2xx response.");
    Assert(!handler.Body.Contains("hello <@channel>", StringComparison.Ordinal), "Slack text should not contain raw mention syntax.");
    Assert(!handler.Body.Contains("policy<&room>", StringComparison.Ordinal), "Slack text should not contain raw angle brackets.");
    Assert(handler.Body.Contains("\"type\":\"image\"", StringComparison.Ordinal), "Slack image block should be emitted.");
    Assert(handler.Body.Contains("https://talk.kakaocdn.net/test-image.png", StringComparison.Ordinal), "Slack payload should include image URL.");
}

static Task TestProgramBoundary()
{
    var programPath = FindRepoRoot()
        .GetFiles("Program.cs", SearchOption.AllDirectories)
        .Single(file => file.FullName.EndsWith(Path.Combine("KakaoCli.Win", "Program.cs"), StringComparison.Ordinal));
    var source = File.ReadAllText(programPath.FullName);
    var start = source.IndexOf("private int HandleSlackMonitor", StringComparison.Ordinal);
    Assert(start >= 0, "HandleSlackMonitor must exist.");
    var end = source.IndexOf("private static int HandleUnknown", start, StringComparison.Ordinal);
    Assert(end > start, "Could not isolate HandleSlackMonitor body.");
    var body = source[start..end];

    foreach (var forbidden in new[]
    {
        "HandleMessages(",
        "HandleSearch(",
        "HandleSync(",
        "HandleQuery(",
        "LoadChatsFixture(",
        "LoadMessagesFixture(",
        "LoadSearchFixture(",
        "LoadSyncFixture(",
    })
    {
        Assert(!body.Contains(forbidden, StringComparison.Ordinal), $"slack-monitor must not call {forbidden}");
    }

    return Task.CompletedTask;
}

static DirectoryInfo FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
        {
            return dir;
        }
        dir = dir.Parent;
    }
    throw new InvalidOperationException("Could not find repo root.");
}

static string CreateTempDir()
{
    var path = Path.Combine(Path.GetTempPath(), $"kakaocli-verify-{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void ExpectThrows<T>(Action action, string message) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

sealed record RawRow(long ChatId, long LogId, Func<string> ReadText);

sealed class FakeReader : IAllowlistedMessageReader
{
    private readonly IReadOnlyDictionary<long, AllowlistedChat> _allowlist;
    private readonly IReadOnlyList<RawRow> _rows;

    public FakeReader(IReadOnlyList<AllowlistedChat> allowlist, IReadOnlyList<RawRow> rows)
    {
        _allowlist = allowlist.ToDictionary(chat => chat.ChatId);
        _rows = rows;
    }

    public HashSet<long> MaxRequested { get; } = [];

    public Task<IReadOnlyDictionary<long, long>> GetCurrentMaxLogIdsByChatAsync(
        IReadOnlySet<long> allowedChatIds,
        CancellationToken cancellationToken = default
    )
    {
        if (!allowedChatIds.All(_allowlist.ContainsKey))
        {
            throw new InvalidOperationException("Fake reader received non-allowlisted max request.");
        }
        MaxRequested.UnionWith(allowedChatIds);
        var result = allowedChatIds.ToDictionary(
            chatId => chatId,
            chatId => _rows.Where(row => row.ChatId == chatId).Select(row => row.LogId).DefaultIfEmpty(0).Max()
        );
        return Task.FromResult<IReadOnlyDictionary<long, long>>(result);
    }

    public Task<IReadOnlyList<AllowlistedMessage>> ReadNewMessagesAsync(
        IReadOnlyDictionary<long, long> perChatCursor,
        CancellationToken cancellationToken = default
    )
    {
        if (!perChatCursor.Keys.All(_allowlist.ContainsKey))
        {
            throw new InvalidOperationException("Fake reader received non-allowlisted cursor.");
        }
        var messages = _rows
            .Where(row => perChatCursor.TryGetValue(row.ChatId, out var cursor) && row.LogId > cursor)
            .Select(row =>
            {
                var chat = _allowlist[row.ChatId];
                return new AllowlistedMessage(
                    row.ChatId,
                    chat.Label,
                    row.LogId,
                    null,
                    "sender",
                    row.ReadText(),
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow
                );
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<AllowlistedMessage>>(messages);
    }
}

sealed class CapturingSink : IMessageSink
{
    private readonly bool _success;

    public CapturingSink(bool success = true)
    {
        _success = success;
    }

    public List<MonitorPayload> Payloads { get; } = [];

    public Task<bool> SendAsync(MonitorPayload payload, CancellationToken cancellationToken = default)
    {
        if (_success)
        {
            Payloads.Add(payload);
        }
        return Task.FromResult(_success);
    }
}

sealed class CapturingHttpHandler : HttpMessageHandler
{
    public string Body { get; private set; } = "";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
    }
}
