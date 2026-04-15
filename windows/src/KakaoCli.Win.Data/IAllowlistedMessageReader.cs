using KakaoCli.Win.Core.Contracts;

namespace KakaoCli.Win.Data;

public interface IAllowlistedMessageReader
{
    Task<IReadOnlyDictionary<long, long>> GetCurrentMaxLogIdsByChatAsync(
        IReadOnlySet<long> allowedChatIds,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<AllowlistedMessage>> ReadNewMessagesAsync(
        IReadOnlyDictionary<long, long> perChatCursor,
        CancellationToken cancellationToken = default
    );
}
