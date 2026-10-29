using VigiaHud.Core.Bot;
using VigiaHud.Core.Logging;

namespace VigiaHud.Core.Telegram;

/// <summary>
/// The loop that keeps the bot listening (DECISIONS 018): long polls Telegram, hands each message to the caller in
/// order, and never gives up. When Telegram cannot be reached it waits longer each time, 2 s doubling up to 5 min,
/// or what Telegram asks (<c>retry_after</c>); a rejected token waits the maximum, since only a person can fix it.
/// Each outage leaves two lines in the log, when it starts and when it ends with its length, and is reported to the
/// caller once the bot is back, so the owner hears about it. A message is acknowledged only after it was handled:
/// if answering it fails for lack of network, it comes again once Telegram is back.
/// </summary>
public sealed class TelegramPoller
{
    public static readonly TimeSpan DefaultPollWait = TimeSpan.FromSeconds(50);
    public static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaxRetry = TimeSpan.FromMinutes(5);

    private readonly ITelegramClient _client;
    private readonly TimeProvider _clock;
    private readonly IWatchLog _log;
    private readonly TimeSpan _pollWait;
    private readonly List<Outage> _unreported = [];

    private TimeSpan _nextRetry = FirstRetry;
    private DateTimeOffset? _outageStart;
    private int _failedAttempts;
    private string _lastReason = string.Empty;

    public TelegramPoller(ITelegramClient client, TimeProvider clock, IWatchLog log, TimeSpan? pollWait = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        _client = client;
        _clock = clock;
        _log = log;
        _pollWait = pollWait ?? DefaultPollWait;
    }

    /// <summary>The offset of the next poll: every update before it has been handled.</summary>
    public long? Offset { get; private set; }

    /// <summary>
    /// Polls until <paramref name="cancellationToken"/> is cancelled, which ends it quietly.
    /// </summary>
    /// <param name="handle">Answers one message. A <see cref="TelegramUnavailableException"/> (or a transient
    /// <see cref="TelegramApiException"/>) from it means "try again later"; any other exception skips the message.</param>
    /// <param name="reconnected">Told about each outage once Telegram answers again; if telling fails for lack of
    /// network, it is told again after the next outage ends.</param>
    public async Task RunAsync(
        Func<IncomingMessage, CancellationToken, Task> handle,
        Func<Outage, CancellationToken, Task> reconnected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(reconnected);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var batch = await _client.GetUpdatesAsync(Offset, _pollWait, cancellationToken).ConfigureAwait(false);
                EndOutage();
                await ReportOutagesAsync(reconnected, cancellationToken).ConfigureAwait(false);
                await HandleAllAsync(batch, handle, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                // Never out of the loop: a bot that stops listening is worse than one that retries too much.
                var wait = Fail(error);
                try
                {
                    await Task.Delay(wait, _clock, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Handles the messages one by one, moving the offset past each one only when it is done.</summary>
    private async Task HandleAllAsync(UpdateBatch batch, Func<IncomingMessage, CancellationToken, Task> handle, CancellationToken cancellationToken)
    {
        foreach (var message in batch.Messages)
        {
            try
            {
                await handle(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (IsTransient(error) || error is OperationCanceledException)
            {
                // Not acknowledged: Telegram will send it again once the bot can answer.
                throw;
            }
            catch (Exception error)
            {
                _log.Write(LogKind.Error, $"telegram: update {message.UpdateId} skipped: {error.GetType().Name}: {error.Message}");
            }

            Offset = message.UpdateId + 1;
        }

        // Anything that was not a message moves the offset too.
        if (batch.NextOffset is { } next && (Offset is null || next > Offset))
        {
            Offset = next;
        }
    }

    private async Task ReportOutagesAsync(Func<Outage, CancellationToken, Task> reconnected, CancellationToken cancellationToken)
    {
        while (_unreported.Count > 0)
        {
            try
            {
                await reconnected(_unreported[0], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (!IsTransient(error) && error is not OperationCanceledException)
            {
                // A fault, not the network: telling again would fail again. The log keeps the outage anyway.
                _log.Write(LogKind.Error, $"telegram: outage report dropped: {error.GetType().Name}: {error.Message}");
            }

            _unreported.RemoveAt(0);
        }
    }

    /// <summary>Notes a failed poll and returns how long to wait before the next one.</summary>
    private TimeSpan Fail(Exception error)
    {
        var now = _clock.GetUtcNow();
        _lastReason = error.Message;
        _failedAttempts++;

        var rejected = error is TelegramApiException { ErrorCode: 401 or 403 or 404 };
        var unexpected = !IsTransient(error);
        TimeSpan wait;
        if (error is TelegramApiException { RetryAfter: { } asked })
        {
            wait = asked;
        }
        else if (rejected || unexpected)
        {
            // Waiting will not fix these by itself: ask rarely.
            wait = MaxRetry;
        }
        else
        {
            wait = _nextRetry;
            _nextRetry = TimeSpan.FromTicks(Math.Min(_nextRetry.Ticks * 2, MaxRetry.Ticks));
        }

        if (unexpected)
        {
            // A fault in our code or an answer we do not understand: every time, with its type.
            _log.Write(LogKind.Error, $"telegram: poll failed: {error.GetType().Name}: {error.Message}; retrying in {Seconds(wait)}");
        }

        if (_outageStart is null)
        {
            _outageStart = now;
            // A rejected token is a fault someone must fix; a network outage is weather.
            _log.Write(
                rejected ? LogKind.Error : LogKind.Info,
                $"telegram: offline ({_lastReason}); retrying in {Seconds(wait)}");
        }

        return wait;
    }

    private void EndOutage()
    {
        _nextRetry = FirstRetry;
        if (_outageStart is not { } start)
        {
            return;
        }

        var outage = new Outage(start, _clock.GetUtcNow(), _failedAttempts, _lastReason);
        _log.Write(LogKind.Info, $"telegram: online again after {Seconds(outage.Duration)} ({outage.FailedAttempts} failed polls)");
        _unreported.Add(outage);
        _outageStart = null;
        _failedAttempts = 0;
    }

    /// <summary>
    /// Worth trying again later: no network, no answer, or Telegram saying "not now" (409 another poller, 429 too many
    /// requests, 5xx) or rejecting the token (which only gets longer waits).
    /// </summary>
    private static bool IsTransient(Exception error) =>
        error is TelegramUnavailableException
        || error is TelegramApiException { ErrorCode: 401 or 403 or 404 or 409 or 429 or >= 500 };

    private static string Seconds(TimeSpan span) => $"{span.TotalSeconds:0} s";
}
