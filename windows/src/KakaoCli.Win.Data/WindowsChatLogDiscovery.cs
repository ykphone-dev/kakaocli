using System.Globalization;
using System.Text.Json;

namespace KakaoCli.Win.Data;

public sealed record WindowsChatLogSummary(
    long ChatId,
    string UserDir,
    string Path,
    long Length,
    DateTimeOffset LastWriteTime,
    string? WalPath,
    long? WalLength,
    DateTimeOffset? WalLastWriteTime,
    DateTimeOffset LastActivityTime,
    bool? InChatWindowUi
);

public static class WindowsChatLogDiscovery
{
    public static async Task<IReadOnlyList<WindowsChatLogSummary>> ListChatLogsAsync(
        string? userDir = null,
        int limit = 50,
        string? sqlitePath = null,
        CancellationToken cancellationToken = default
    )
    {
        var roots = ResolveUserDirs(userDir);
        var windowChatIds = await LoadChatWindowIdsAsync(roots, sqlitePath, cancellationToken);
        var summaries = new List<WindowsChatLogSummary>();

        foreach (var root in roots)
        {
            var chatData = Path.Combine(root, "chat_data");
            if (!Directory.Exists(chatData))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(chatData, "chatLogs_*.edb"))
            {
                var chatIdText = Path.GetFileNameWithoutExtension(file)["chatLogs_".Length..];
                if (!long.TryParse(chatIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId))
                {
                    continue;
                }

                var info = new FileInfo(file);
                var walInfo = new FileInfo(file + "-wal");
                var walExists = walInfo.Exists;
                DateTimeOffset? walLastWrite = walExists ? walInfo.LastWriteTimeUtc : null;
                var lastActivity = walLastWrite.HasValue && walLastWrite.Value > info.LastWriteTimeUtc
                    ? walLastWrite.Value
                    : info.LastWriteTimeUtc;
                var userDirName = new DirectoryInfo(root).Name;
                var key = (Root: root, ChatId: chatId);
                summaries.Add(new WindowsChatLogSummary(
                    chatId,
                    userDirName,
                    info.FullName,
                    info.Length,
                    info.LastWriteTimeUtc,
                    walExists ? walInfo.FullName : null,
                    walExists ? walInfo.Length : null,
                    walLastWrite,
                    lastActivity,
                    windowChatIds.TryGetValue(key, out var inUi) ? inUi : null
                ));
            }
        }

        return summaries
            .OrderByDescending(item => item.LastActivityTime)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    private static IReadOnlyList<string> ResolveUserDirs(string? userDir)
    {
        if (!string.IsNullOrWhiteSpace(userDir))
        {
            return [Path.GetFullPath(userDir)];
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return [];
        }

        var usersRoot = Path.Combine(localAppData, "Kakao", "KakaoTalk", "users");
        return Directory.Exists(usersRoot)
            ? Directory.GetDirectories(usersRoot)
            : [];
    }

    private static async Task<Dictionary<(string Root, long ChatId), bool>> LoadChatWindowIdsAsync(
        IReadOnlyList<string> roots,
        string? sqlitePath,
        CancellationToken cancellationToken
    )
    {
        var result = new Dictionary<(string Root, long ChatId), bool>();
        var sqlite = new SqliteCliClient(sqlitePath);

        foreach (var root in roots)
        {
            var dbPath = Path.Combine(root, "chatWindowUi.db");
            if (!File.Exists(dbPath))
            {
                continue;
            }

            try
            {
                using var rows = await sqlite.QueryJsonAsync(
                    dbPath,
                    "SELECT chatId FROM chatWindowUi;",
                    cancellationToken
                );

                if (rows.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var row in rows.RootElement.EnumerateArray())
                {
                    if (!row.TryGetProperty("chatId", out var chatIdProperty))
                    {
                        continue;
                    }

                    if (TryReadLong(chatIdProperty, out var chatId))
                    {
                        result[(root, chatId)] = true;
                    }
                }
            }
            catch
            {
                // Listing chat log files should still work even when sqlite3 is unavailable.
            }
        }

        return result;
    }

    private static bool TryReadLong(JsonElement element, out long value)
    {
        value = 0;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
        {
            return true;
        }

        return element.ValueKind == JsonValueKind.String
            && long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
