using KakaoCli.Win.Core.Contracts;

namespace KakaoCli.Win.Data;

public sealed class AllowlistedMessageReader : IAllowlistedMessageReader
{
    private readonly IReadOnlyDictionary<long, AllowlistedChat> _allowlist;

    public AllowlistedMessageReader(IReadOnlyList<AllowlistedChat> allowlist)
    {
        if (allowlist.Count == 0)
        {
            throw new ArgumentException("Allowlist must be validated before constructing the reader.", nameof(allowlist));
        }

        _allowlist = allowlist.ToDictionary(chat => chat.ChatId);
    }

    public Task<IReadOnlyDictionary<long, long>> GetCurrentMaxLogIdsByChatAsync(
        IReadOnlySet<long> allowedChatIds,
        CancellationToken cancellationToken = default
    )
    {
        AssertAllowedOnly(allowedChatIds);
        throw new NotSupportedException(
            "Live Windows KakaoTalk message access is not implemented yet. " +
            "Run the Windows feasibility gate and implement a constrained chatId+cursor data source before enabling Slack delivery."
        );
    }

    public Task<IReadOnlyList<AllowlistedMessage>> ReadNewMessagesAsync(
        IReadOnlyDictionary<long, long> perChatCursor,
        CancellationToken cancellationToken = default
    )
    {
        AssertAllowedOnly(perChatCursor.Keys);
        throw new NotSupportedException(
            "Live Windows KakaoTalk message access is not implemented yet. " +
            "The implementation must constrain by per-chat cursor before projecting message text."
        );
    }

    private void AssertAllowedOnly(IEnumerable<long> chatIds)
    {
        foreach (var chatId in chatIds)
        {
            if (!_allowlist.ContainsKey(chatId))
            {
                throw new InvalidOperationException($"Reader received non-allowlisted chat id {chatId}.");
            }
        }
    }
}
