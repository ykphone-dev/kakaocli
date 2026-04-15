using KakaoCli.Win.Automation;
using KakaoCli.Win.Core.Contracts;
using KakaoCli.Win.Core.Utilities;
using KakaoCli.Win.Data;

var app = new KakaoCliWinApp(new ProbeArtifactStore(), new WindowsKakaoAutomation());
return app.Run(args);

internal sealed class KakaoCliWinApp
{
    private readonly ProbeArtifactStore _artifacts;
    private readonly WindowsKakaoAutomation _automation;

    public KakaoCliWinApp(ProbeArtifactStore artifacts, WindowsKakaoAutomation automation)
    {
        _artifacts = artifacts;
        _automation = automation;
    }

    public int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var command = args[0];
        var rest = args.Skip(1).ToArray();

        return command switch
        {
            "status" => HandleStatus(),
            "auth" => HandleAuth(rest),
            "chats" => HandleChats(rest),
            "messages" => HandleMessages(rest),
            "search" => HandleSearch(rest),
            "schema" => HandleSchema(),
            "query" => HandleQuery(rest),
            "sync" => HandleSync(rest),
            "login" => HandleLogin(rest),
            "send" => HandleSend(rest),
            "harvest" => HandleHarvest(rest),
            "probe" => HandleProbe(),
            "slack-monitor" => HandleSlackMonitor(rest),
            _ => HandleUnknown(command),
        };
    }

    private int HandleStatus()
    {
        var collector = new WindowsProbeCollector(_artifacts);
        JsonOutput.Write(collector.CollectSummary());
        return 0;
    }

    private int HandleAuth(string[] args)
    {
        var verbose = args.Contains("--verbose");
        Console.WriteLine("Windows auth probe");
        Console.WriteLine("==================");
        Console.WriteLine($"Probe directory: {_artifacts.ProbeDirectory}");
        Console.WriteLine($"paths.json exists: {File.Exists(Path.Combine(_artifacts.ProbeDirectory, "paths.json"))}");
        if (verbose)
        {
            Console.WriteLine("Auth is currently probe-backed and requires a real Windows KakaoTalk installation.");
        }
        return 0;
    }

    private int HandleChats(string[] args)
    {
        var asJson = args.Contains("--json");
        var limit = ReadIntOption(args, "--limit") ?? 50;
        var records = _artifacts.LoadChatsFixture().Take(limit).ToList();
        if (asJson)
        {
            var payload = records.Select(ToChatPayload);
            JsonOutput.Write(payload);
            return 0;
        }

        if (records.Count == 0)
        {
            Console.WriteLine("No chats found.");
            return 0;
        }

        foreach (var chat in records)
        {
            Console.WriteLine($"[{chat.Id}] {chat.DisplayName}");
        }
        return 0;
    }

    private int HandleMessages(string[] args)
    {
        var asJson = args.Contains("--json");
        var limit = ReadIntOption(args, "--limit") ?? 50;
        var since = ReadStringOption(args, "--since");
        var sinceDate = DurationParser.ParseLookback(since);
        var chatName = ReadStringOption(args, "--chat");
        var chatId = ReadLongOption(args, "--chat-id");

        var records = _artifacts.LoadMessagesFixture()
            .Where(msg => !chatId.HasValue || msg.ChatId == chatId.Value)
            .Where(msg => sinceDate is null || DateTimeOffset.Parse(msg.Timestamp) >= sinceDate.Value)
            .Take(limit)
            .ToList();

        if (chatName is not null)
        {
            Console.Error.WriteLine($"Note: --chat '{chatName}' requires live chat lookup and is currently fixture-backed only.");
        }

        return WriteMessages(records, asJson);
    }

    private int HandleSearch(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("search requires a query argument");
            return 1;
        }

        var query = args[0];
        var asJson = args.Contains("--json");
        var limit = ReadIntOption(args, "--limit") ?? 20;
        var records = _artifacts.LoadSearchFixture()
            .Where(msg => (msg.Text ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();

        return WriteMessages(records, asJson);
    }

    private int HandleSchema()
    {
        var schemaPath = Path.Combine(_artifacts.ProbeDirectory, "db-schema.sql");
        if (File.Exists(schemaPath))
        {
            Console.WriteLine(File.ReadAllText(schemaPath));
            return 0;
        }

        Console.WriteLine("No tables found (probe schema is missing).");
        return 0;
    }

    private int HandleQuery(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("query requires a SQL string");
            return 1;
        }

        JsonOutput.Write(new[]
        {
            new Dictionary<string, object?>
            {
                ["status"] = "not_implemented",
                ["sql"] = args[0],
                ["message"] = "Raw SQL execution requires a live Windows data layer.",
            },
        });
        return 0;
    }

    private int HandleSync(string[] args)
    {
        var follow = args.Contains("--follow");
        if (!follow)
        {
            JsonOutput.Write(new Dictionary<string, object?>
            {
                ["status"] = "ready",
                ["max_log_id"] = _artifacts.LoadSyncFixture().Select(x => x.LogId).DefaultIfEmpty(0).Max(),
            });
            return 0;
        }

        foreach (var item in _artifacts.LoadSyncFixture())
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(item, JsonOutput.Options));
        }

        return 0;
    }

    private int HandleLogin(string[] args)
    {
        if (args.Contains("--status"))
        {
            Console.WriteLine("Login Status");
            Console.WriteLine("============");
            Console.WriteLine("Stored credentials: Unknown");
            Console.WriteLine("App state:          unknown");
            Console.WriteLine();
            Console.WriteLine("Windows login automation is pending probe completion.");
            return 0;
        }

        if (args.Contains("--clear"))
        {
            Console.WriteLine("Credentials removed from Windows credential store integration is not implemented yet.");
            return 0;
        }

        Console.WriteLine(_automation.NotImplementedMessage("login"));
        return 1;
    }

    private int HandleSend(string[] args)
    {
        Console.WriteLine(_automation.NotImplementedMessage("send"));
        return 1;
    }

    private int HandleHarvest(string[] args)
    {
        var dryRun = args.Contains("--dry-run");
        if (dryRun)
        {
            JsonOutput.Write(new[]
            {
                new Dictionary<string, object?>
                {
                    ["chatId"] = "0",
                    ["name"] = "probe-only",
                    ["messagesBefore"] = 0,
                    ["messagesAfter"] = 0,
                    ["newMessages"] = 0,
                    ["skipped"] = true,
                },
            });
            return 0;
        }

        Console.WriteLine(_automation.NotImplementedMessage("harvest"));
        return 1;
    }

    private int HandleProbe()
    {
        var collector = new WindowsProbeCollector(_artifacts);
        collector.WriteArtifacts();
        Console.WriteLine($"Probe artifacts written to {_artifacts.ProbeDirectory}");
        return 0;
    }

    private int HandleSlackMonitor(string[] args)
    {
        var configPath = ReadStringOption(args, "--config");
        var dryRun = args.Contains("--dry-run");
        var once = args.Contains("--once");
        var intervalOverride = ReadIntOption(args, "--interval-seconds");

        var validation = AllowlistConfig.LoadAndValidate(configPath ?? string.Empty);
        if (!validation.IsValid || validation.Config is null)
        {
            foreach (var error in validation.Errors)
            {
                Console.Error.WriteLine($"Config error: {error}");
            }
            return 2;
        }

        var config = validation.Config;
        if (intervalOverride is > 0)
        {
            config = config with { PollIntervalSeconds = intervalOverride.Value };
        }

        try
        {
            IMessageSink sink = dryRun
                ? new DryRunMessageSink()
                : new SlackMessageSink(config.SlackWebhookUrlEnv);

            var reader = new AllowlistedMessageReader(config.Allowlist);
            var cursorStore = new MonitorCursorStore(config.CursorPath);
            var poller = new PollingService(config.Allowlist, reader, cursorStore, sink, config.MaxTextChars);

            if (once)
            {
                return poller.RunOnceAsync().GetAwaiter().GetResult();
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            while (!cts.IsCancellationRequested)
            {
                poller.RunOnceAsync(cts.Token).GetAwaiter().GetResult();
                Task.Delay(TimeSpan.FromSeconds(config.PollIntervalSeconds), cts.Token).GetAwaiter().GetResult();
            }

            return 0;
        }
        catch (NotSupportedException error)
        {
            Console.Error.WriteLine(error.Message);
            return 3;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"slack-monitor failed: {error.Message}");
            return 1;
        }
    }

    private static int HandleUnknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        return 1;
    }

    private static int WriteMessages(IReadOnlyList<MessageRecord> records, bool asJson)
    {
        if (asJson)
        {
            var payload = records.Select(ToMessagePayload);
            JsonOutput.Write(payload);
            return 0;
        }

        if (records.Count == 0)
        {
            Console.WriteLine("No messages found.");
            return 0;
        }

        foreach (var msg in records)
        {
            var sender = msg.IsFromMe ? "Me" : (msg.Sender ?? "Unknown");
            Console.WriteLine($"{msg.Timestamp} {sender}: {msg.Text ?? $"[{msg.Type}]"}");
        }

        return 0;
    }

    private static bool IsHelp(string arg)
    {
        return arg is "-h" or "--help" or "help";
    }

    private static Dictionary<string, object?> ToChatPayload(ChatRecord chat)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = chat.Id,
            ["type"] = chat.Type,
            ["display_name"] = chat.DisplayName,
            ["member_count"] = chat.MemberCount,
            ["unread_count"] = chat.UnreadCount,
            ["last_message_at"] = chat.LastMessageAt,
        };
    }

    private static Dictionary<string, object?> ToMessagePayload(MessageRecord msg)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = msg.Id,
            ["chat_id"] = msg.ChatId,
            ["sender_id"] = msg.SenderId,
            ["type"] = msg.Type,
            ["timestamp"] = msg.Timestamp,
            ["is_from_me"] = msg.IsFromMe,
            ["sender"] = msg.Sender,
            ["text"] = msg.Text,
        };
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
KakaoCli.Win

Commands:
  status
  auth [--verbose]
  chats [--limit N] [--json]
  messages [--chat NAME] [--chat-id ID] [--since 1h] [--limit N] [--json]
  search <query> [--limit N] [--json]
  schema
  query <sql>
  sync [--follow]
  login [--status|--clear]
  send <chat> <message>
  harvest [--dry-run]
  probe
  slack-monitor --config PATH [--dry-run] [--once] [--interval-seconds N]
""");
    }

    private static string? ReadStringOption(string[] args, string option)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == option)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int? ReadIntOption(string[] args, string option)
    {
        return int.TryParse(ReadStringOption(args, option), out var value) ? value : null;
    }

    private static long? ReadLongOption(string[] args, string option)
    {
        return long.TryParse(ReadStringOption(args, option), out var value) ? value : null;
    }
}
