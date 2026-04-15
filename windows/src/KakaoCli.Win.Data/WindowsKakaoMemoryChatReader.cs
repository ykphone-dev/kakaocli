using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KakaoCli.Win.Core.Contracts;

namespace KakaoCli.Win.Data;

public sealed class WindowsKakaoMemoryChatReader
{
    private static readonly Regex MessageRegex = new(
        "\"message\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.Compiled
    );

    private readonly long _chatId;
    private readonly int _scanHitLimit;
    private readonly int _contextChars;
    private readonly IReadOnlySet<long> _selfUserIds;

    public WindowsKakaoMemoryChatReader(
        long chatId,
        int scanHitLimit = 250,
        int contextChars = 1600,
        IReadOnlySet<long>? selfUserIds = null
    )
    {
        _chatId = chatId;
        _scanHitLimit = scanHitLimit;
        _contextChars = contextChars;
        _selfUserIds = selfUserIds ?? new HashSet<long>();
    }

    public IReadOnlyList<SyncEvent> ReadSince(long sinceLogId = 0, int limit = 50)
    {
        var hits = WindowsKakaoMemoryReader.ScanForText(_chatId.ToString(CultureInfo.InvariantCulture), _scanHitLimit, _contextChars);
        var events = new Dictionary<long, SyncEvent>();

        foreach (var hit in hits)
        {
            if (!TryParseEvent(hit.Context, out var item) || item.LogId <= sinceLogId)
            {
                continue;
            }

            events[item.LogId] = item;
        }

        return events.Values
            .OrderBy(item => item.LogId)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    private bool TryParseEvent(string context, out SyncEvent item)
    {
        item = default!;

        if (!ContainsLongProperty(context, "chatId", _chatId)
            || !TryReadLongProperty(context, "logId", out var logId)
            || !TryReadLongProperty(context, "authorId", out var senderId))
        {
            return false;
        }

        var messageMatch = MessageRegex.Match(context);
        if (!messageMatch.Success)
        {
            return false;
        }

        var text = DecodeJsonString(messageMatch.Groups[1].Value);
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        TryReadIntProperty(context, "type", out var messageType);
        var timestamp = TryReadLongProperty(context, "sendAt", out var sendAt)
            ? NormalizeEpoch(sendAt)
            : DateTimeOffset.UtcNow.ToString("O");

        item = new SyncEvent(
            "message",
            logId,
            _chatId,
            null,
            senderId,
            null,
            text,
            messageType,
            timestamp,
            _selfUserIds.Contains(senderId)
        );
        return true;
    }

    private static bool ContainsLongProperty(string context, string propertyName, long expected)
    {
        return TryReadLongProperty(context, propertyName, out var value) && value == expected;
    }

    private static bool TryReadLongProperty(string context, string propertyName, out long value)
    {
        value = 0;
        var match = Regex.Match(
            context,
            $"\"{Regex.Escape(propertyName)}\"\\s*:\\s*(\\d+)",
            RegexOptions.CultureInvariant
        );
        return match.Success
               && long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadIntProperty(string context, string propertyName, out int value)
    {
        value = 0;
        return TryReadLongProperty(context, propertyName, out var longValue)
               && longValue >= int.MinValue
               && longValue <= int.MaxValue
               && (value = (int)longValue) == longValue;
    }

    private static string? DecodeJsonString(string escaped)
    {
        try
        {
            return JsonSerializer.Deserialize<string>($"\"{escaped}\"");
        }
        catch (JsonException)
        {
            return escaped;
        }
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
}
