using KakaoCli.Win.Automation;
using KakaoCli.Win.Core.Contracts;
using KakaoCli.Win.Core.Utilities;
using KakaoCli.Win.Data;
using System.Text.Json;

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
            "win-list-chatlogs" => HandleWinListChatLogs(rest),
            "win-diagnose-key" => HandleWinDiagnoseKey(rest),
            "win-memory-scan" => HandleWinMemoryScan(rest),
            "win-memory-watch" => HandleWinMemoryWatch(rest),
            "win-memory-read-chat" => HandleWinMemoryReadChat(rest),
            "win-memory-slack-monitor" => HandleWinMemorySlackMonitor(rest),
            "win-read-chat" => HandleWinReadChat(rest),
            "win-identity" => HandleWinIdentity(),
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

    private int HandleWinReadChat(string[] args)
    {
        var chatId = ReadLongOption(args, "--chat-id");
        if (chatId is null or <= 0)
        {
            Console.Error.WriteLine("win-read-chat requires --chat-id ID");
            return 2;
        }

        try
        {
            var identity = ResolveWindowsIdentity();
            var edbPath = WindowsChatLogCrypto.ResolveChatLogPath(
                chatId.Value,
                ReadStringOption(args, "--edb"),
                ReadStringOption(args, "--user-dir")
            );
            var pragmaCandidates = ResolveWindowsPragmaCandidates(args, identity);
            if (pragmaCandidates.Count == 0)
            {
                Console.Error.WriteLine("win-read-chat requires --pragma VALUE, KAKAOCLI_WIN_PRAGMA, or readable KakaoTalk DeviceInfo registry data.");
                return 2;
            }

            var userIdCandidates = ResolveWindowsUserIdCandidates(args, edbPath, pragmaCandidates);
            var keyMaterial = FindWorkingKeyMaterial(edbPath, pragmaCandidates, userIdCandidates);
            if (keyMaterial is null)
            {
                throw new InvalidOperationException(
                    "Found pragma/user id candidates, but none decrypted this file header. " +
                    "Run win-diagnose-key with the same --chat-id to inspect the attempted headers."
                );
            }

            var (pragma, userId) = keyMaterial.Value;
            var decryptOutput = ReadStringOption(args, "--decrypt-output");

            if (args.Contains("--decrypt-only"))
            {
                var decrypted = WindowsChatLogCrypto.DecryptToTemp(edbPath, pragma, userId, decryptOutput);
                JsonOutput.Write(new Dictionary<string, object?>
                {
                    ["source_path"] = decrypted.SourcePath,
                    ["decrypted_path"] = decrypted.DecryptedPath,
                    ["user_id"] = decrypted.UserId,
                    ["key_mode"] = decrypted.KeyMode,
                });
                return 0;
            }

            var reader = new WindowsSingleChatReader(
                chatId.Value,
                edbPath,
                pragma,
                userId,
                ReadStringOption(args, "--sqlite"),
                decryptOutput
            );

            var follow = args.Contains("--follow");
            var limit = ReadIntOption(args, "--limit") ?? 100;
            if (!follow)
            {
                var since = ReadLongOption(args, "--since-log-id") ?? 0;
                var events = reader.ReadSinceAsync(since, limit).GetAwaiter().GetResult();
                JsonOutput.Write(events);
                return 0;
            }

            return FollowSingleChat(args, reader, limit);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"win-read-chat failed: {error.Message}");
            return 1;
        }
    }

    private int HandleWinListChatLogs(string[] args)
    {
        try
        {
            var limit = ReadIntOption(args, "--limit") ?? 50;
            var items = WindowsChatLogDiscovery.ListChatLogsAsync(
                ReadStringOption(args, "--user-dir"),
                limit,
                ReadStringOption(args, "--sqlite")
            ).GetAwaiter().GetResult();

            if (args.Contains("--json"))
            {
                JsonOutput.Write(items.Select(item => new Dictionary<string, object?>
                {
                    ["chat_id"] = item.ChatId,
                    ["user_dir"] = item.UserDir,
                    ["path"] = item.Path,
                    ["bytes"] = item.Length,
                    ["last_write_time"] = item.LastWriteTime.ToString("O"),
                    ["wal_path"] = item.WalPath,
                    ["wal_bytes"] = item.WalLength,
                    ["wal_last_write_time"] = item.WalLastWriteTime?.ToString("O"),
                    ["last_activity_time"] = item.LastActivityTime.ToString("O"),
                    ["in_chat_window_ui"] = item.InChatWindowUi,
                }));
                return 0;
            }

            foreach (var item in items)
            {
                var ui = item.InChatWindowUi is null ? "ui=unknown" : $"ui={item.InChatWindowUi.Value}";
                var wal = item.WalLength.HasValue ? $" walBytes={item.WalLength.Value}" : string.Empty;
                Console.WriteLine($"{item.LastActivityTime:O} chatId={item.ChatId} bytes={item.Length}{wal} {ui} userDir={item.UserDir}");
            }

            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"win-list-chatlogs failed: {error.Message}");
            return 1;
        }
    }

    private int HandleWinDiagnoseKey(string[] args)
    {
        var chatId = ReadLongOption(args, "--chat-id");
        if (chatId is null or <= 0)
        {
            Console.Error.WriteLine("win-diagnose-key requires --chat-id ID");
            return 2;
        }

        try
        {
            var identity = ResolveWindowsIdentity();
            var edbPath = WindowsChatLogCrypto.ResolveChatLogPath(
                chatId.Value,
                ReadStringOption(args, "--edb"),
                ReadStringOption(args, "--user-dir")
            );
            var pragmaCandidates = ResolveWindowsPragmaCandidates(args, identity);
            var userIdCandidates = ResolveWindowsUserIdCandidates(args, edbPath, pragmaCandidates, throwIfMissing: false);
            var attempts = WindowsChatLogCrypto.DescribeHeaderAttempts(edbPath, pragmaCandidates, userIdCandidates);

            JsonOutput.Write(new Dictionary<string, object?>
            {
                ["chat_id"] = chatId.Value,
                ["edb_path"] = edbPath,
                ["device_info_registry_key"] = identity?.RegistryKeyName,
                ["dev_id"] = identity?.DevId,
                ["generated_pragma"] = identity?.Pragma,
                ["pragma_candidates"] = pragmaCandidates,
                ["user_id_candidates"] = userIdCandidates,
                ["header_attempts"] = attempts.Select(attempt => new Dictionary<string, object?>
                {
                    ["pragma"] = attempt.Pragma,
                    ["pragma_prefix"] = Prefix(attempt.Pragma, 36),
                    ["user_id"] = attempt.UserId,
                    ["key_mode"] = attempt.KeyMode,
                    ["matches_sqlite_header"] = attempt.MatchesSqliteHeader,
                    ["decrypted_header_hex"] = attempt.DecryptedHeaderHex,
                }),
                ["any_match"] = attempts.Any(attempt => attempt.MatchesSqliteHeader),
            });
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"win-diagnose-key failed: {error.Message}");
            return 1;
        }
    }

    private static int HandleWinMemoryScan(string[] args)
    {
        var needle = ReadStringOption(args, "--text")
            ?? ReadStringOption(args, "--chat-id");
        if (string.IsNullOrWhiteSpace(needle))
        {
            Console.Error.WriteLine("win-memory-scan requires --chat-id ID or --text TEXT");
            return 2;
        }

        try
        {
            var limit = ReadIntOption(args, "--limit") ?? 20;
            var contextChars = ReadIntOption(args, "--context-chars") ?? 320;
            var hits = WindowsKakaoMemoryReader.ScanForText(needle, limit, contextChars);
            JsonOutput.Write(hits.Select(hit => new Dictionary<string, object?>
            {
                ["encoding"] = hit.EncodingName,
                ["address"] = hit.Address,
                ["context"] = hit.Context,
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"win-memory-scan failed: {error.Message}");
            return 1;
        }
    }

    private static int HandleWinMemoryWatch(string[] args)
    {
        var needle = ReadStringOption(args, "--text")
            ?? ReadStringOption(args, "--chat-id");
        if (string.IsNullOrWhiteSpace(needle))
        {
            Console.Error.WriteLine("win-memory-watch requires --chat-id ID or --text TEXT");
            return 2;
        }

        var intervalSeconds = ReadIntOption(args, "--interval-seconds") ?? 2;
        var limit = ReadIntOption(args, "--limit") ?? 20;
        var contextChars = ReadIntOption(args, "--context-chars") ?? 320;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lineOptions = new JsonSerializerOptions(JsonOutput.Options)
        {
            WriteIndented = false,
        };

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        Console.Error.WriteLine($"Watching KakaoTalk memory for '{needle}'. Press Ctrl+C to stop.");
        try
        {
            while (!cts.IsCancellationRequested)
            {
                foreach (var hit in WindowsKakaoMemoryReader.ScanForText(needle, limit, contextChars))
                {
                    var key = $"{hit.EncodingName}:{hit.Context}";
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["type"] = "memory_hit",
                        ["needle"] = needle,
                        ["encoding"] = hit.EncodingName,
                        ["address"] = hit.Address,
                        ["observed_at"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["context"] = hit.Context,
                    }, lineOptions));
                }

                Task.Delay(TimeSpan.FromSeconds(Math.Max(1, intervalSeconds)), cts.Token).GetAwaiter().GetResult();
            }
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"win-memory-watch failed: {error.Message}");
            return 1;
        }

        return 0;
    }

    private static int HandleWinMemoryReadChat(string[] args)
    {
        var chatId = ReadLongOption(args, "--chat-id");
        if (chatId is null or <= 0)
        {
            Console.Error.WriteLine("win-memory-read-chat requires --chat-id ID");
            return 2;
        }

        try
        {
            var scanLimit = ReadIntOption(args, "--scan-limit") ?? 250;
            var contextChars = ReadIntOption(args, "--context-chars") ?? 1600;
            var limit = ReadIntOption(args, "--limit") ?? 50;
            var reader = new WindowsKakaoMemoryChatReader(
                chatId.Value,
                scanLimit,
                contextChars,
                ResolveSelfUserIds(args)
            );

            if (!args.Contains("--follow"))
            {
                var sinceLogId = ReadLongOption(args, "--since-log-id") ?? 0;
                JsonOutput.Write(reader.ReadSince(sinceLogId, limit));
                return 0;
            }

            return FollowMemoryChat(args, reader, limit);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"win-memory-read-chat failed: {error.Message}");
            return 1;
        }
    }

    private static int HandleWinMemorySlackMonitor(string[] args)
    {
        var chatId = ReadLongOption(args, "--chat-id");
        if (chatId is null or <= 0)
        {
            Console.Error.WriteLine("win-memory-slack-monitor requires --chat-id ID");
            return 2;
        }

        var label = ReadStringOption(args, "--label") ?? "Kakao self chat";
        var webhookEnv = ReadStringOption(args, "--webhook-env") ?? "KAKAOCLI_SLACK_WEBHOOK_URL";
        var webhookUrl = ReadStringOption(args, "--webhook-url");
        var cursorPath = ReadStringOption(args, "--cursor") ?? ".kakaocli/windows-memory-slack-cursors.json";
        var intervalSeconds = ReadIntOption(args, "--interval-seconds") ?? 2;
        var maxTextChars = ReadIntOption(args, "--max-text-chars") ?? 500;
        var scanLimit = ReadIntOption(args, "--scan-limit") ?? 250;
        var contextChars = ReadIntOption(args, "--context-chars") ?? 1600;
        var readLimit = ReadIntOption(args, "--limit") ?? 100;

        try
        {
            var allowlist = new[] { new AllowlistedChat(chatId.Value, label) };
            IMessageSink sink = args.Contains("--dry-run")
                ? new DryRunMessageSink()
                : SlackMessageSink.FromWebhookUrl(
                    webhookUrl
                    ?? Environment.GetEnvironmentVariable(webhookEnv)
                    ?? throw new InvalidOperationException($"Pass --webhook-url URL or set {webhookEnv}.")
                );
            var reader = new WindowsMemoryAllowlistedMessageReader(
                allowlist,
                ResolveSelfUserIds(args),
                scanLimit,
                contextChars,
                readLimit
            );
            var cursorStore = new MonitorCursorStore(cursorPath);
            var poller = new PollingService(allowlist, reader, cursorStore, sink, maxTextChars);

            if (args.Contains("--once"))
            {
                var sent = poller.RunOnceAsync().GetAwaiter().GetResult();
                Console.Error.WriteLine($"win-memory-slack-monitor sent {sent} message(s).");
                return 0;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            Console.Error.WriteLine($"Watching KakaoTalk memory chat {chatId.Value} for Slack delivery.");
            while (!cts.IsCancellationRequested)
            {
                var sent = poller.RunOnceAsync(cts.Token).GetAwaiter().GetResult();
                if (sent > 0)
                {
                    Console.Error.WriteLine($"Delivered {sent} message(s) to Slack.");
                }

                Task.Delay(TimeSpan.FromSeconds(Math.Max(1, intervalSeconds)), cts.Token).GetAwaiter().GetResult();
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"win-memory-slack-monitor failed: {error.Message}");
            return 1;
        }
    }

    private static int FollowMemoryChat(string[] args, WindowsKakaoMemoryChatReader reader, int limit)
    {
        var intervalSeconds = ReadIntOption(args, "--interval-seconds") ?? 2;
        var explicitCursor = ReadLongOption(args, "--since-log-id");
        var cursor = explicitCursor ?? reader.ReadSince(0, limit).Select(item => item.LogId).DefaultIfEmpty(0).Max();
        var lineOptions = new JsonSerializerOptions(JsonOutput.Options)
        {
            WriteIndented = false,
        };

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        Console.Error.WriteLine($"Watching KakaoTalk memory messages from log_id>{cursor}.");
        try
        {
            while (!cts.IsCancellationRequested)
            {
                foreach (var item in reader.ReadSince(cursor, limit).OrderBy(item => item.LogId))
                {
                    Console.WriteLine(JsonSerializer.Serialize(item, lineOptions));
                    if (item.LogId > cursor)
                    {
                        cursor = item.LogId;
                    }
                }

                Task.Delay(TimeSpan.FromSeconds(Math.Max(1, intervalSeconds)), cts.Token).GetAwaiter().GetResult();
            }
        }
        catch (OperationCanceledException)
        {
            return 0;
        }

        return 0;
    }

    private static IReadOnlySet<long> ResolveSelfUserIds(string[] args)
    {
        var explicitUserId = ReadLongOption(args, "--self-user-id");
        if (explicitUserId.HasValue)
        {
            return new HashSet<long> { explicitUserId.Value };
        }

        var values = new HashSet<long>();
        try
        {
            var identity = WindowsKakaoIdentity.ResolveDeviceInfo();
            var pragmas = WindowsKakaoIdentity.FindPragmasInKakaoTalkMemory(identity.DevId)
                .Concat([identity.Pragma])
                .Distinct(StringComparer.Ordinal);

            foreach (var candidate in pragmas.SelectMany(WindowsKakaoIdentity.FindUserIdsInKakaoTalkMemory)
                         .Concat(WindowsKakaoIdentity.FindLikelyUserIdsInKakaoTalkMemory().Take(1)))
            {
                if (long.TryParse(candidate, out var parsed))
                {
                    values.Add(parsed);
                }
            }
        }
        catch
        {
            // The message reader still works without self classification.
        }

        return values;
    }

    private static int HandleWinIdentity()
    {
        try
        {
            var identity = WindowsKakaoIdentity.ResolveDeviceInfo();
            var memoryPragmas = WindowsKakaoIdentity.FindPragmasInKakaoTalkMemory(identity.DevId);
            var pragmaCandidates = new[] { identity.Pragma }
                .Concat(memoryPragmas)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var userIds = pragmaCandidates
                .SelectMany(WindowsKakaoIdentity.FindUserIdsInKakaoTalkMemory)
                .Concat(WindowsKakaoIdentity.FindLikelyUserIdsInKakaoTalkMemory())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            JsonOutput.Write(new Dictionary<string, object?>
            {
                ["device_info_registry_key"] = identity.RegistryKeyName,
                ["dev_id"] = identity.DevId,
                ["sys_uuid"] = identity.SystemUuid,
                ["hdd_model"] = identity.DiskModel,
                ["hdd_serial"] = identity.DiskSerial,
                ["generated_pragma"] = identity.Pragma,
                ["memory_pragma_candidates"] = memoryPragmas,
                ["memory_user_id_candidates"] = userIds,
            });
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"win-identity failed: {error.Message}");
            return 1;
        }
    }

    private static IReadOnlyList<string> ResolveWindowsPragmaCandidates(string[] args, WindowsDeviceInfo? identity)
    {
        var explicitPragma = ReadStringOption(args, "--pragma")
            ?? Environment.GetEnvironmentVariable("KAKAOCLI_WIN_PRAGMA");
        if (!string.IsNullOrWhiteSpace(explicitPragma))
        {
            return [explicitPragma];
        }

        var candidates = new List<string>();
        if (identity is not null)
        {
            candidates.AddRange(WindowsKakaoIdentity.FindPragmasInKakaoTalkMemory(identity.DevId));
            candidates.Add(identity.Pragma);
            candidates.Add(identity.DevId);
        }

        return candidates
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> ResolveWindowsUserIdCandidates(
        string[] args,
        string edbPath,
        IReadOnlyList<string> pragmaCandidates,
        bool throwIfMissing = true
    )
    {
        var userId = ReadStringOption(args, "--user-id") ?? Environment.GetEnvironmentVariable("KAKAOCLI_WIN_USER_ID");
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return WindowsChatLogCrypto.ExpandUserIdCandidates(userId).Distinct(StringComparer.Ordinal).ToList();
        }

        var start = ReadLongOption(args, "--user-id-start");
        var end = ReadLongOption(args, "--user-id-end");
        if (start.HasValue && end.HasValue)
        {
            Console.Error.WriteLine($"Searching KakaoTalk user id in range {start.Value}..{end.Value}.");
            foreach (var pragma in pragmaCandidates)
            {
                try
                {
                    var found = WindowsChatLogCrypto.FindUserIdByHeader(edbPath, pragma, start.Value, end.Value);
                    Console.Error.WriteLine($"Found KakaoTalk user id: {found}");
                    return [found];
                }
                catch (InvalidOperationException)
                {
                    // Try the next pragma candidate.
                }
            }

            throw new InvalidOperationException(
                $"No user id in range {start.Value}..{end.Value} decrypted the SQLite header."
            );
        }

        var memoryCandidates = pragmaCandidates
            .SelectMany(WindowsKakaoIdentity.FindUserIdsInKakaoTalkMemory)
            .Concat(WindowsKakaoIdentity.FindLikelyUserIdsInKakaoTalkMemory())
            .SelectMany(WindowsChatLogCrypto.ExpandUserIdCandidates)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (memoryCandidates.Count > 0)
        {
            return memoryCandidates;
        }

        if (throwIfMissing)
        {
            throw new ArgumentException(
                "win-read-chat requires --user-id VALUE, KAKAOCLI_WIN_USER_ID, " +
                "readable KakaoTalk process memory, or both --user-id-start and --user-id-end for header-based search."
            );
        }

        return [];
    }

    private static (string Pragma, string UserId)? FindWorkingKeyMaterial(
        string edbPath,
        IReadOnlyList<string> pragmaCandidates,
        IReadOnlyList<string> userIdCandidates
    )
    {
        foreach (var pragma in pragmaCandidates)
        {
            foreach (var userId in userIdCandidates)
            {
                if (WindowsChatLogCrypto.CanDecryptHeader(edbPath, pragma, userId))
                {
                    Console.Error.WriteLine($"Using Windows KakaoTalk pragma prefix {Prefix(pragma, 36)} and user id {userId}.");
                    return (pragma, userId);
                }
            }
        }

        Console.Error.WriteLine(
            "Found pragma candidates and KakaoTalk user id candidates, but none decrypted this file header. " +
            $"pragmaCount={pragmaCandidates.Count}, userIdCandidates={string.Join(", ", userIdCandidates)}"
        );
        return null;
    }

    private static int FollowSingleChat(string[] args, WindowsSingleChatReader reader, int limit)
    {
        var intervalSeconds = ReadIntOption(args, "--interval-seconds") ?? 2;
        var cursor = ReadLongOption(args, "--since-log-id")
            ?? reader.GetCurrentMaxLogIdAsync().GetAwaiter().GetResult();

        Console.Error.WriteLine($"Watching Windows chat log from log_id>{cursor}.");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var lineOptions = new JsonSerializerOptions(JsonOutput.Options)
        {
            WriteIndented = false,
        };

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var events = reader.ReadSinceAsync(cursor, limit, cts.Token).GetAwaiter().GetResult();
                foreach (var item in events.OrderBy(item => item.LogId))
                {
                    Console.WriteLine(JsonSerializer.Serialize(item, lineOptions));
                    if (item.LogId > cursor)
                    {
                        cursor = item.LogId;
                    }
                }

                Task.Delay(TimeSpan.FromSeconds(Math.Max(1, intervalSeconds)), cts.Token).GetAwaiter().GetResult();
            }
        }
        catch (OperationCanceledException)
        {
            return 0;
        }

        return 0;
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
  win-identity
  win-list-chatlogs [--limit N] [--json] [--user-dir PATH] [--sqlite PATH]
  win-diagnose-key --chat-id ID [--pragma VALUE] [--user-id VALUE] [--edb PATH] [--user-dir PATH]
  win-memory-scan (--chat-id ID|--text TEXT) [--limit N] [--context-chars N]
  win-memory-watch (--chat-id ID|--text TEXT) [--interval-seconds N] [--limit N] [--context-chars N]
  win-memory-read-chat --chat-id ID [--since-log-id N] [--limit N] [--follow]
                       [--interval-seconds N] [--scan-limit N] [--context-chars N] [--self-user-id N]
  win-memory-slack-monitor --chat-id ID [--label NAME] [--webhook-url URL] [--webhook-env ENV] [--cursor PATH]
                           [--dry-run] [--once] [--interval-seconds N] [--max-text-chars N]
                           [--scan-limit N] [--context-chars N] [--limit N] [--self-user-id N]
  win-read-chat --chat-id ID [--pragma VALUE] [--user-id VALUE] [--since-log-id N] [--follow]
                [--interval-seconds N] [--limit N] [--edb PATH] [--user-dir PATH]
                [--sqlite PATH] [--decrypt-only] [--decrypt-output PATH]
                [--user-id-start N --user-id-end N]
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

    private static WindowsDeviceInfo? ResolveWindowsIdentity()
    {
        try
        {
            return WindowsKakaoIdentity.ResolveDeviceInfo();
        }
        catch
        {
            return null;
        }
    }

    private static string Prefix(string value, int length)
    {
        return value.Length <= length ? value : value[..length];
    }
}
