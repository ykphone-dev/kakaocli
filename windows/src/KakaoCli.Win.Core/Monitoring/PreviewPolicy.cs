namespace KakaoCli.Win.Core.Monitoring;

public static class PreviewPolicy
{
    public static string CreatePreview(string? text, int maxTextChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (maxTextChars <= 0)
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length <= maxTextChars)
        {
            return normalized;
        }

        return normalized[..maxTextChars];
    }
}
