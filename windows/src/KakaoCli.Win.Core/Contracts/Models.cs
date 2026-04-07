using System.Text.Json.Serialization;

namespace KakaoCli.Win.Core.Contracts;

public sealed record ChatRecord(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("member_count")] int MemberCount,
    [property: JsonPropertyName("unread_count")] int UnreadCount,
    [property: JsonPropertyName("last_message_at")] string? LastMessageAt
);

public sealed record MessageRecord(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("sender_id")] long SenderId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("is_from_me")] bool IsFromMe,
    [property: JsonPropertyName("sender")] string? Sender,
    [property: JsonPropertyName("text")] string? Text
);

public sealed record SyncEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("log_id")] long LogId,
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("chat_name")] string? ChatName,
    [property: JsonPropertyName("sender_id")] long SenderId,
    [property: JsonPropertyName("sender")] string? Sender,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("message_type")] int MessageType,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("is_from_me")] bool IsFromMe
);

public sealed record ContractRule(
    string Name,
    string Category,
    string Requirement,
    string Notes
);

public enum ProbeStatus
{
    Missing,
    Detected,
    Accessible,
    Unknown
}

public sealed record ProbeSummary(
    ProbeStatus InstallPath,
    ProbeStatus DataPath,
    ProbeStatus DatabasePath,
    ProbeStatus UiAutomation,
    IReadOnlyList<string> Notes
);
