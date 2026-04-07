namespace KakaoCli.Win.Core.Utilities;

public static class DurationParser
{
    public static DateTimeOffset? ParseLookback(string? value, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        if (trimmed.Length < 2)
        {
            return null;
        }

        var unit = trimmed[^1];
        if (!double.TryParse(trimmed[..^1], out var amount))
        {
            return null;
        }

        var baseline = now ?? DateTimeOffset.UtcNow;
        return unit switch
        {
            's' => baseline.AddSeconds(-amount),
            'm' => baseline.AddMinutes(-amount),
            'h' => baseline.AddHours(-amount),
            'd' => baseline.AddDays(-amount),
            'w' => baseline.AddDays(-(7 * amount)),
            _ => null,
        };
    }
}
