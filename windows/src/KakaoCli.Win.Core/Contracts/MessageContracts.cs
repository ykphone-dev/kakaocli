using System.Text.Json.Serialization;

namespace KakaoCli.Win.Core.Contracts;

public sealed record AllowlistedMessage(
    long ChatId,
    string Label,
    long LogId,
    long? SenderId,
    string? Sender,
    string? Text,
    DateTimeOffset Timestamp,
    DateTimeOffset ObservedAt,
    IReadOnlyList<string>? ImageUrls = null
);

public sealed record MonitorPayload(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("chat_label")] string ChatLabel,
    [property: JsonPropertyName("log_id")] long LogId,
    [property: JsonPropertyName("sender_id")] long? SenderId,
    [property: JsonPropertyName("sender")] string? Sender,
    [property: JsonPropertyName("text_preview")] string TextPreview,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("observed_at")] string ObservedAt,
    [property: JsonPropertyName("image_urls")] IReadOnlyList<string>? ImageUrls = null
);

public interface IMessageSink
{
    Task<bool> SendAsync(MonitorPayload payload, CancellationToken cancellationToken = default);
}
