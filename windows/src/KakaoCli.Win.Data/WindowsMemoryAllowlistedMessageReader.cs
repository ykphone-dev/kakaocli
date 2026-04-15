using KakaoCli.Win.Core.Contracts;

namespace KakaoCli.Win.Data;

public sealed class WindowsMemoryAllowlistedMessageReader : IAllowlistedMessageReader
{
    private readonly IReadOnlyDictionary<long, AllowlistedChat> _allowlist;
    private readonly IReadOnlySet<long> _selfUserIds;
    private readonly int _scanLimit;
    private readonly int _contextChars;
    private readonly int _readLimit;

    public WindowsMemoryAllowlistedMessageReader(
        IReadOnlyList<AllowlistedChat> allowlist,
        IReadOnlySet<long> selfUserIds,
        int scanLimit = 250,
        int contextChars = 1600,
        int readLimit = 100
    )
    {
        if (allowlist.Count == 0)
        {
            throw new ArgumentException("Allowlist must not be empty.", nameof(allowlist));
        }

        _allowlist = allowlist.ToDictionary(chat => chat.ChatId);
        _selfUserIds = selfUserIds;
        _scanLimit = scanLimit;
        _contextChars = contextChars;
        _readLimit = readLimit;
    }

    public Task<IReadOnlyDictionary<long, long>> GetCurrentMaxLogIdsByChatAsync(
        IReadOnlySet<long> allowedChatIds,
        CancellationToken cancellationToken = default
    )
    {
        AssertAllowedOnly(allowedChatIds);
        var result = new Dictionary<long, long>();
        foreach (var chatId in allowedChatIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reader = CreateReader(chatId);
            result[chatId] = reader.ReadSince(0, _readLimit)
                .Select(item => item.LogId)
                .DefaultIfEmpty(0)
                .Max();
        }

        return Task.FromResult<IReadOnlyDictionary<long, long>>(result);
    }

    public Task<IReadOnlyList<AllowlistedMessage>> ReadNewMessagesAsync(
        IReadOnlyDictionary<long, long> perChatCursor,
        CancellationToken cancellationToken = default
    )
    {
        AssertAllowedOnly(perChatCursor.Keys);
        var messages = new List<AllowlistedMessage>();

        foreach (var (chatId, cursor) in perChatCursor)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reader = CreateReader(chatId);
            var label = _allowlist[chatId].Label;
            foreach (var item in reader.ReadSince(cursor, _readLimit))
            {
                messages.Add(new AllowlistedMessage(
                    item.ChatId,
                    label,
                    item.LogId,
                    item.SenderId,
                    item.IsFromMe ? "Me" : item.Sender,
                    item.Text,
                    ParseTimestamp(item.Timestamp),
                    DateTimeOffset.UtcNow,
                    item.ImageUrls
                ));
            }
        }

        return Task.FromResult<IReadOnlyList<AllowlistedMessage>>(messages);
    }

    private WindowsKakaoMemoryChatReader CreateReader(long chatId)
    {
        return new WindowsKakaoMemoryChatReader(chatId, _scanLimit, _contextChars, _selfUserIds);
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

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UtcNow;
    }
}
