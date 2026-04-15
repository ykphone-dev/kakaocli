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
        var text = $"[{EscapeSlackText(payload.ChatLabel)}] " +
                   $"{EscapeSlackText(payload.Sender ?? "Unknown")}: " +
                   $"{EscapeSlackText(payload.TextPreview)}";
        var response = await _client.PostAsJsonAsync(
            _webhookUri,
            new
            {
                text,
                metadata = new
                {
                    chat_id = payload.ChatId,
                    log_id = payload.LogId,
                    timestamp = payload.Timestamp,
                    observed_at = payload.ObservedAt,
                },
            },
            cancellationToken
        );

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        Console.Error.WriteLine(
            $"Slack delivery failed: HTTP {(int)response.StatusCode} for chat_id={payload.ChatId}, log_id={payload.LogId}"
        );
        return false;
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
