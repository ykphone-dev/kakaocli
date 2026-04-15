using System.Text.Json;
using System.Text.Json.Serialization;
using KakaoCli.Win.Core.Contracts;

namespace KakaoCli.Win.Data;

public sealed class MonitorCursorStore
{
    private readonly string _path;

    public MonitorCursorStore(string path)
    {
        _path = ResolvePath(path);
    }

    public async Task<IReadOnlyDictionary<long, long>> LoadAsync(
        IReadOnlyList<AllowlistedChat> allowlist,
        CancellationToken cancellationToken = default
    )
    {
        var allowed = allowlist.Select(chat => chat.ChatId).ToHashSet();
        if (!File.Exists(_path))
        {
            return new Dictionary<long, long>();
        }

        await using var stream = File.OpenRead(_path);
        var document = await JsonSerializer.DeserializeAsync<CursorDocument>(
            stream,
            CursorJson.Options,
            cancellationToken
        );

        if (document?.Chats is null)
        {
            return new Dictionary<long, long>();
        }

        var pruned = document.Chats
            .Select(pair => (Parsed: long.TryParse(pair.Key, out var id), ChatId: id, Entry: pair.Value))
            .Where(item => item.Parsed && allowed.Contains(item.ChatId))
            .ToDictionary(item => item.ChatId, item => item.Entry.LastLogId);

        if (pruned.Count != document.Chats.Count)
        {
            await SaveAsync(allowlist, pruned, cancellationToken);
        }

        return pruned;
    }

    public async Task SaveAsync(
        IReadOnlyList<AllowlistedChat> allowlist,
        IReadOnlyDictionary<long, long> cursors,
        CancellationToken cancellationToken = default
    )
    {
        var allowed = allowlist.ToDictionary(chat => chat.ChatId);
        var document = new CursorDocument
        {
            Version = 1,
            Chats = cursors
                .Where(pair => allowed.ContainsKey(pair.Key))
                .ToDictionary(
                    pair => pair.Key.ToString(),
                    pair => new CursorEntry
                    {
                        LastLogId = pair.Value,
                        Label = allowed[pair.Key].Label,
                        UpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
                    }
                ),
        };

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_path}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, document, CursorJson.Options, cancellationToken);
        }

        File.Move(tempPath, _path, overwrite: true);
    }

    private static string ResolvePath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            return Path.Combine(home, path[2..]);
        }

        if (path.StartsWith(".kakaocli", StringComparison.Ordinal))
        {
            return Path.Combine(home, path);
        }

        return Path.GetFullPath(path);
    }

    private sealed class CursorDocument
    {
        [JsonPropertyName("version")]
        public int Version { get; init; } = 1;

        [JsonPropertyName("chats")]
        public Dictionary<string, CursorEntry> Chats { get; init; } = [];
    }

    private sealed class CursorEntry
    {
        [JsonPropertyName("last_log_id")]
        public long LastLogId { get; init; }

        [JsonPropertyName("label")]
        public string Label { get; init; } = "";

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; init; } = "";
    }

    private static class CursorJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }
}
