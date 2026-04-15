using System.Globalization;
using System.Text.Json;
using KakaoCli.Win.Core.Contracts;

namespace KakaoCli.Win.Data;

public sealed class WindowsSingleChatReader
{
    private readonly long _chatId;
    private readonly string _encryptedPath;
    private readonly string _pragma;
    private readonly string _userId;
    private readonly string? _sqlitePath;
    private readonly string? _decryptedOutputPath;

    public WindowsSingleChatReader(
        long chatId,
        string encryptedPath,
        string pragma,
        string userId,
        string? sqlitePath = null,
        string? decryptedOutputPath = null
    )
    {
        _chatId = chatId;
        _encryptedPath = encryptedPath;
        _pragma = pragma;
        _userId = userId;
        _sqlitePath = sqlitePath;
        _decryptedOutputPath = decryptedOutputPath;
    }

    public async Task<long> GetCurrentMaxLogIdAsync(CancellationToken cancellationToken = default)
    {
        var decrypted = WindowsChatLogCrypto.DecryptToTemp(_encryptedPath, _pragma, _userId, _decryptedOutputPath);
        var sqlite = new SqliteCliClient(_sqlitePath);
        var schema = await LoadSchemaAsync(sqlite, decrypted.DecryptedPath, cancellationToken);
        var logIdColumn = schema.PickRequired("id", "logId", "log_id", "messageId", "message_id");

        using var result = await sqlite.QueryJsonAsync(
            decrypted.DecryptedPath,
            $"SELECT MAX({Quote(logIdColumn)}) AS max_log_id FROM {Quote(schema.TableName)};",
            cancellationToken
        );

        var first = result.RootElement.ValueKind == JsonValueKind.Array && result.RootElement.GetArrayLength() > 0
            ? result.RootElement[0]
            : default;
        return first.ValueKind == JsonValueKind.Object && TryReadLong(first, "max_log_id", out var value) ? value : 0;
    }

    public async Task<IReadOnlyList<SyncEvent>> ReadSinceAsync(
        long sinceLogId,
        int limit = 100,
        CancellationToken cancellationToken = default
    )
    {
        var decrypted = WindowsChatLogCrypto.DecryptToTemp(_encryptedPath, _pragma, _userId, _decryptedOutputPath);
        var sqlite = new SqliteCliClient(_sqlitePath);
        var schema = await LoadSchemaAsync(sqlite, decrypted.DecryptedPath, cancellationToken);

        var logIdColumn = schema.PickRequired("id", "logId", "log_id", "messageId", "message_id");
        var senderColumn = schema.PickOptional("userId", "user_id", "authorId", "author_id", "senderId", "sender_id");
        var textColumn = schema.PickOptional("message", "msg", "text", "content");
        var typeColumn = schema.PickOptional("type", "messageType", "message_type");
        var timestampColumn = schema.PickOptional("createdAt", "created_at", "sentAt", "sent_at", "sendAt", "timestamp");

        var sql = $"""
            SELECT
              {Quote(logIdColumn)} AS log_id,
              {SelectOrNull(senderColumn)} AS sender_id,
              {SelectOrNull(textColumn)} AS text,
              {SelectOrNull(typeColumn)} AS message_type,
              {SelectOrNull(timestampColumn)} AS raw_timestamp
            FROM {Quote(schema.TableName)}
            WHERE {Quote(logIdColumn)} > {sinceLogId}
            ORDER BY {Quote(logIdColumn)} ASC
            LIMIT {Math.Max(1, limit)};
            """;

        using var rows = await sqlite.QueryJsonAsync(decrypted.DecryptedPath, sql, cancellationToken);
        var events = new List<SyncEvent>();
        if (rows.RootElement.ValueKind != JsonValueKind.Array)
        {
            return events;
        }

        foreach (var row in rows.RootElement.EnumerateArray())
        {
            if (!TryReadLong(row, "log_id", out var logId))
            {
                continue;
            }

            TryReadLong(row, "sender_id", out var senderId);
            var text = TryReadString(row, "text", out var body) ? body : null;
            var messageType = TryReadInt(row, "message_type", out var type) ? type : 0;
            var timestamp = TryReadRaw(row, "raw_timestamp", out var rawTimestamp)
                ? NormalizeTimestamp(rawTimestamp)
                : DateTimeOffset.UtcNow.ToString("O");

            events.Add(new SyncEvent(
                "message",
                logId,
                _chatId,
                null,
                senderId,
                null,
                text,
                messageType,
                timestamp,
                false
            ));
        }

        return events;
    }

    private static async Task<ChatLogSchema> LoadSchemaAsync(
        SqliteCliClient sqlite,
        string decryptedPath,
        CancellationToken cancellationToken
    )
    {
        var tableName = await ResolveTableNameAsync(sqlite, decryptedPath, cancellationToken);
        using var columns = await sqlite.QueryJsonAsync(
            decryptedPath,
            $"PRAGMA table_info({Quote(tableName)});",
            cancellationToken
        );

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (columns.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var column in columns.RootElement.EnumerateArray())
            {
                if (TryReadString(column, "name", out var name) && !string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }

        if (names.Count == 0)
        {
            throw new InvalidOperationException($"Could not read schema for table {tableName}.");
        }

        return new ChatLogSchema(tableName, names);
    }

    private static async Task<string> ResolveTableNameAsync(
        SqliteCliClient sqlite,
        string decryptedPath,
        CancellationToken cancellationToken
    )
    {
        using var tables = await sqlite.QueryJsonAsync(
            decryptedPath,
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;",
            cancellationToken
        );

        var tableNames = new List<string>();
        if (tables.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var table in tables.RootElement.EnumerateArray())
            {
                if (TryReadString(table, "name", out var name) && !string.IsNullOrWhiteSpace(name))
                {
                    tableNames.Add(name);
                }
            }
        }

        var preferred = tableNames.FirstOrDefault(name => name.Equals("chatLogs", StringComparison.OrdinalIgnoreCase))
            ?? tableNames.FirstOrDefault(name => name.Contains("chat", StringComparison.OrdinalIgnoreCase)
                                                && name.Contains("log", StringComparison.OrdinalIgnoreCase));

        if (preferred is null)
        {
            throw new InvalidOperationException(
                $"Could not find a chat log table. Tables: {string.Join(", ", tableNames)}"
            );
        }

        return preferred;
    }

    private static string SelectOrNull(string? column)
    {
        return column is null ? "NULL" : Quote(column);
    }

    private static string Quote(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static bool TryReadLong(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String
            && long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryReadInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        value = property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
        return true;
    }

    private static bool TryReadRaw(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        value = property;
        return true;
    }

    private static string NormalizeTimestamp(JsonElement raw)
    {
        if (raw.ValueKind == JsonValueKind.String)
        {
            var text = raw.GetString();
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            {
                return parsed.ToString("O");
            }

            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return NormalizeEpoch(number);
            }
        }

        if (raw.ValueKind == JsonValueKind.Number && raw.TryGetInt64(out var numeric))
        {
            return NormalizeEpoch(numeric);
        }

        return DateTimeOffset.UtcNow.ToString("O");
    }

    private static string NormalizeEpoch(long value)
    {
        try
        {
            if (value > 10_000_000_000)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(value).ToString("O");
            }

            if (value > 0)
            {
                return DateTimeOffset.FromUnixTimeSeconds(value).ToString("O");
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Fall through to observed time.
        }

        return DateTimeOffset.UtcNow.ToString("O");
    }

    private sealed record ChatLogSchema(string TableName, IReadOnlySet<string> Columns)
    {
        public string PickRequired(params string[] candidates)
        {
            return PickOptional(candidates) ?? throw new InvalidOperationException(
                $"Could not find any of these columns in {TableName}: {string.Join(", ", candidates)}. " +
                $"Actual columns: {string.Join(", ", Columns.OrderBy(column => column, StringComparer.OrdinalIgnoreCase))}"
            );
        }

        public string? PickOptional(params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                var match = Columns.FirstOrDefault(column => column.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }
    }
}
