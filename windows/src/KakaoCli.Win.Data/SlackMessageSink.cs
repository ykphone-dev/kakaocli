using System.Net.Http.Json;
using KakaoCli.Win.Core.Contracts;

namespace KakaoCli.Win.Data;

public sealed class SlackMessageSink : IMessageSink
{
    private readonly Uri _webhookUri;
    private readonly HttpClient _client;

    public SlackMessageSink(string webhookUrlEnv, HttpClient? client = null)
        : this(ReadWebhookFromEnv(webhookUrlEnv), client)
    {
    }

    public SlackMessageSink(Uri webhookUri, HttpClient? client = null)
    {
        if (webhookUri.Scheme != Uri.UriSchemeHttps || !IsSlackHost(webhookUri.Host))
        {
            throw new InvalidOperationException("Slack webhook URL must be an HTTPS slack.com URL.");
        }

        _webhookUri = webhookUri;
        _client = client ?? new HttpClient();
    }

    public static SlackMessageSink FromWebhookUrl(string webhookUrl, HttpClient? client = null)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            throw new InvalidOperationException("Slack webhook URL is empty.");
        }

        return new SlackMessageSink(new Uri(webhookUrl), client);
    }

    private static Uri ReadWebhookFromEnv(string webhookUrlEnv)
    {
        var webhookUrl = Environment.GetEnvironmentVariable(webhookUrlEnv);
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            throw new InvalidOperationException($"Slack webhook URL env var is not set: {webhookUrlEnv}");
        }

        return new Uri(webhookUrl);
    }

    public async Task<bool> SendAsync(MonitorPayload payload, CancellationToken cancellationToken = default)
    {
        var imageUrls = FilterImageUrls(payload.ImageUrls);
        var blockImageUrls = imageUrls.Where(IsSlackImageBlockCandidate).ToList();
        var text = BuildText(payload, imageUrls, blockImageUrls.Count == 0);
        var body = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["metadata"] = new
            {
                chat_id = payload.ChatId,
                log_id = payload.LogId,
                timestamp = payload.Timestamp,
                observed_at = payload.ObservedAt,
            },
        };
        if (imageUrls.Count > 0)
        {
            body["unfurl_links"] = true;
            body["unfurl_media"] = true;
        }

        var blocks = BuildBlocks(text, blockImageUrls);
        if (blocks.Count > 0)
        {
            body["blocks"] = blocks;
        }

        var response = await _client.PostAsJsonAsync(
            _webhookUri,
            body,
            cancellationToken
        );

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        Console.Error.WriteLine(
            $"Slack delivery failed: HTTP {(int)response.StatusCode} for chat_id={payload.ChatId}, " +
            $"log_id={payload.LogId}: {responseBody}"
        );
        return false;
    }

    private static string BuildText(MonitorPayload payload, IReadOnlyList<string> imageUrls, bool appendImageLinks)
    {
        var text = $"[{EscapeSlackText(payload.ChatLabel)}] " +
                   $"{EscapeSlackText(payload.Sender ?? "Unknown")}: " +
                   $"{EscapeSlackText(payload.TextPreview)}";

        if (!appendImageLinks || imageUrls.Count == 0)
        {
            return text;
        }

        return text + "\n" + string.Join("\n", imageUrls.Select(EscapeSlackText));
    }

    private static IReadOnlyList<Dictionary<string, object?>> BuildBlocks(
        string text,
        IReadOnlyList<string> imageUrls
    )
    {
        if (imageUrls.Count == 0)
        {
            return [];
        }

        var blocks = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["type"] = "section",
                ["text"] = new Dictionary<string, string>
                {
                    ["type"] = "mrkdwn",
                    ["text"] = text,
                },
            },
        };

        for (var i = 0; i < imageUrls.Count; i++)
        {
            blocks.Add(new Dictionary<string, object?>
            {
                ["type"] = "image",
                ["image_url"] = imageUrls[i],
                ["alt_text"] = $"Kakao image {i + 1}",
            });
        }

        return blocks;
    }

    private static IReadOnlyList<string> FilterImageUrls(IReadOnlyList<string>? imageUrls)
    {
        return imageUrls?
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                          && uri.Scheme == Uri.UriSchemeHttps)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList() ?? [];
    }

    private static bool IsSlackImageBlockCandidate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // Slack rejects Kakao signed CDN URLs as image blocks, but accepts them as links.
        return !uri.Host.EndsWith("kakaocdn.net", StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeSlackText(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static bool IsSlackHost(string host)
    {
        return host.Equals("slack.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".slack.com", StringComparison.OrdinalIgnoreCase);
    }
}
