using VigiaHud.Core.Telegram;

namespace VigiaHud.Core.Tests.Support;

/// <summary>
/// Telegram as a script: each poll takes the next prepared outcome (a batch or an exception); with nothing left it
/// hangs until cancelled, like a quiet long poll. Every poll is recorded, and a test can wait for the n-th one.
/// </summary>
public sealed class FakeTelegramClient : ITelegramClient
{
    private readonly Queue<Func<UpdateBatch>> _polls = new();
    private readonly List<(long? Offset, TimeSpan Wait)> _calls = [];
    private readonly SemaphoreSlim _called = new(0);
    private readonly object _gate = new();

    public IReadOnlyList<(long? Offset, TimeSpan Wait)> Polls
    {
        get { lock (_gate) { return [.. _calls]; } }
    }

    public FakeTelegramClient Returns(UpdateBatch batch)
    {
        lock (_gate) { _polls.Enqueue(() => batch); }
        return this;
    }

    public FakeTelegramClient Fails(Exception error)
    {
        lock (_gate) { _polls.Enqueue(() => throw error); }
        return this;
    }

    public FakeTelegramClient FailsTimes(int times, Exception error)
    {
        for (var i = 0; i < times; i++)
        {
            Fails(error);
        }

        return this;
    }

    /// <summary>Waits (in real time, briefly) until the loop has polled <paramref name="count"/> times in total.</summary>
    public async Task WaitForPollsAsync(int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Polls.Count < count)
        {
            await _called.WaitAsync(deadline.Token);
        }
    }

    public async Task<UpdateBatch> GetUpdatesAsync(long? offset, TimeSpan wait, CancellationToken cancellationToken)
    {
        Func<UpdateBatch>? next;
        lock (_gate)
        {
            _calls.Add((offset, wait));
            next = _polls.Count > 0 ? _polls.Dequeue() : null;
        }

        _called.Release();
        if (next is null)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return next!();
    }

    private readonly Queue<Exception> _sendFailures = new();
    private readonly List<(long ChatId, string Text)> _sent = [];

    /// <summary>What went out, in order; a photo appears as "[foto] caption".</summary>
    public IReadOnlyList<(long ChatId, string Text)> Sent
    {
        get { lock (_gate) { return [.. _sent]; } }
    }

    /// <summary>The next send fails with <paramref name="error"/> (one per call).</summary>
    public FakeTelegramClient FailsToSend(Exception error)
    {
        lock (_gate) { _sendFailures.Enqueue(error); }
        return this;
    }

    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken) => Send(chatId, text);

    public Task SendPhotoAsync(long chatId, byte[] jpeg, string? caption, CancellationToken cancellationToken) =>
        Send(chatId, $"[foto] {caption}");

    private Task Send(long chatId, string text)
    {
        lock (_gate)
        {
            if (_sendFailures.Count > 0)
            {
                return Task.FromException(_sendFailures.Dequeue());
            }

            _sent.Add((chatId, text));
        }

        return Task.CompletedTask;
    }
}
