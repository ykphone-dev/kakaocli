using KakaoCli.Win.Core.Contracts;
using KakaoCli.Win.Core.Monitoring;

namespace KakaoCli.Win.Data;

public sealed class PollingService
{
    private readonly IReadOnlyList<AllowlistedChat> _allowlist;
    private readonly IAllowlistedMessageReader _reader;
    private readonly MonitorCursorStore _cursorStore;
    private readonly IMessageSink _sink;
    private readonly int _maxTextChars;

    public PollingService(
        IReadOnlyList<AllowlistedChat> allowlist,
        IAllowlistedMessageReader reader,
        MonitorCursorStore cursorStore,
        IMessageSink sink,
        int maxTextChars
    )
    {
        if (allowlist.Count == 0)
        {
            throw new ArgumentException("Allowlist must not be empty.", nameof(allowlist));
        }

        _allowlist = allowlist;
        _reader = reader;
        _cursorStore = cursorStore;
        _sink = sink;
        _maxTextChars = maxTextChars;
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var cursors = (await _cursorStore.LoadAsync(_allowlist, cancellationToken)).ToDictionary();
        var missingChatIds = _allowlist
            .Select(chat => chat.ChatId)
            .Where(chatId => !cursors.ContainsKey(chatId))
            .ToHashSet();

        if (missingChatIds.Count > 0)
        {
            var maxIds = await _reader.GetCurrentMaxLogIdsByChatAsync(missingChatIds, cancellationToken);
            foreach (var chatId in missingChatIds)
            {
                cursors[chatId] = maxIds.TryGetValue(chatId, out var maxLogId) ? maxLogId : 0;
            }

            await _cursorStore.SaveAsync(_allowlist, cursors, cancellationToken);
        }

        var messages = await _reader.ReadNewMessagesAsync(cursors, cancellationToken);
        var sent = 0;
        foreach (var message in messages.OrderBy(message => message.LogId))
        {
            if (!_allowlist.Any(chat => chat.ChatId == message.ChatId))
            {
                continue;
            }

            var payload = ToPayload(message);
            if (await _sink.SendAsync(payload, cancellationToken))
            {
                sent++;
                if (!cursors.TryGetValue(message.ChatId, out var current) || message.LogId > current)
                {
                    cursors[message.ChatId] = message.LogId;
                }
            }
        }

        await _cursorStore.SaveAsync(_allowlist, cursors, cancellationToken);
        return sent;
    }

    private MonitorPayload ToPayload(AllowlistedMessage message)
    {
        return new MonitorPayload(
            message.ChatId,
            message.Label,
            message.LogId,
            message.SenderId,
            message.Sender,
            PreviewPolicy.CreatePreview(message.Text, _maxTextChars),
            message.Timestamp.ToString("O"),
            message.ObservedAt.ToString("O")
        );
    }
}
