using VigiaHud.Core.Logging;
using VigiaHud.Core.Telegram;

namespace VigiaHud.Core.Bot;

/// <summary>
/// The messages the bot sends on its own (heartbeat, outage and power notices), kept in order until they go out
/// (DECISIONS 019). A message that leaves more than two minutes late says when it was written: in a power cut the
/// router goes down too, and "se ha ido la luz" may only reach the owner when it comes back. Kept in memory only:
/// if the phone dies before sending, the log still has what happened. Bounded, so a week offline cannot fill memory.
/// </summary>
public sealed class Outbox
{
    public const int DefaultCapacity = 50;
    private static readonly TimeSpan LateAfter = TimeSpan.FromMinutes(2);

    private readonly ITelegramClient _client;
    private readonly long _chatId;
    private readonly TimeProvider _clock;
    private readonly IWatchLog _log;
    private readonly TimeZoneInfo _zone;
    private readonly int _capacity;
    private readonly LinkedList<(DateTimeOffset WrittenAt, string Text)> _pending = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sending = new(1, 1);

    public Outbox(ITelegramClient client, long chatId, TimeProvider clock, IWatchLog log, TimeZoneInfo zone, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _client = client;
        _chatId = chatId;
        _clock = clock;
        _log = log;
        _zone = zone;
        _capacity = capacity;
    }

    public int Count
    {
        get { lock (_gate) { return _pending.Count; } }
    }

    /// <summary>Queues a text written now. When full, the oldest one gives way, with a line in the log.</summary>
    public void Enqueue(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        lock (_gate)
        {
            if (_pending.Count == _capacity)
            {
                var dropped = _pending.First!.Value;
                _pending.RemoveFirst();
                _log.Write(LogKind.Error, $"telegram: outbox full; dropped the message written at {dropped.WrittenAt:yyyy-MM-ddTHH:mm:ssZ}");
            }

            _pending.AddLast((_clock.GetUtcNow(), text));
        }
    }

    /// <summary>
    /// Sends what is pending, oldest first. Stops at the first message that cannot go for lack of network, which
    /// stays first for the next time; a message Telegram refuses for good (too long, say) is dropped and logged so
    /// it does not block the rest. Never throws but for the caller's cancellation.
    /// </summary>
    /// <returns>How many went out.</returns>
    public async Task<int> FlushAsync(CancellationToken cancellationToken)
    {
        await _sending.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sent = 0;
            while (true)
            {
                (DateTimeOffset WrittenAt, string Text) next;
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        return sent;
                    }

                    next = _pending.First!.Value;
                }

                try
                {
                    await _client.SendMessageAsync(_chatId, Stamp(next.WrittenAt, next.Text), cancellationToken).ConfigureAwait(false);
                    sent++;
                }
                catch (Exception error) when (IsRetriable(error))
                {
                    return sent;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    _log.Write(LogKind.Error, $"telegram: notice dropped: {error.GetType().Name}: {error.Message}");
                }

                lock (_gate)
                {
                    _pending.RemoveFirst();
                }
            }
        }
        finally
        {
            _sending.Release();
        }
    }

    private string Stamp(DateTimeOffset writtenAt, string text)
    {
        var now = _clock.GetUtcNow();
        return now - writtenAt > LateAfter
            ? $"(Con retraso: es de {BotTexts.Moment(writtenAt, now, _zone)}.)\n{text}"
            : text;
    }

    private static bool IsRetriable(Exception error) =>
        error is TelegramUnavailableException
        || error is TelegramApiException { ErrorCode: 401 or 403 or 404 or 409 or 429 or >= 500 };
}
