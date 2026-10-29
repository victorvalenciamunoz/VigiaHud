using VigiaHud.Core.Bot;
using VigiaHud.Core.Logging;
using VigiaHud.Core.Telegram;
using VigiaHud.Core.Tests.Support;

namespace VigiaHud.Core.Tests.Telegram;

/// <summary>
/// Each failed poll makes the loop wait on the clock, which is one timer: the tests wait for that timer before
/// moving the clock (<see cref="SteppingClock"/>), so the order of events is fixed and nothing depends on luck.
/// </summary>
public sealed class TelegramPollerTests : IAsyncDisposable
{
    private const long Owner = 111_222_333;
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);
    private static readonly TelegramUnavailableException NoNetwork = new("getUpdates: No such host is known.");

    private readonly SteppingClock _clock = new(Start);
    private readonly FakeTelegramClient _telegram = new();
    private readonly MemoryLog _log = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<long> _handled = [];
    private readonly List<Outage> _reported = [];
    private Func<IncomingMessage, Task> _onMessage = _ => Task.CompletedTask;
    private Func<Outage, Task> _onReconnected = _ => Task.CompletedTask;
    private Task? _run;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_run is not null)
        {
            await _run.WaitAsync(TimeSpan.FromSeconds(5));
        }

        _stop.Dispose();
    }

    [Fact]
    public async Task Messages_are_handled_in_order_and_the_next_poll_asks_past_them()
    {
        _telegram.Returns(Batch(12, 10, 11));

        Run();
        await _telegram.WaitForPollsAsync(2);

        Assert.Equal([10, 11], _handled);
        Assert.Equal((null, TelegramPoller.DefaultPollWait), _telegram.Polls[0]);
        Assert.Equal(12, _telegram.Polls[1].Offset);
    }

    [Fact]
    public async Task Updates_that_are_not_messages_still_move_the_offset()
    {
        _telegram.Returns(new UpdateBatch([], 20));

        Run();
        await _telegram.WaitForPollsAsync(2);

        Assert.Equal(20, _telegram.Polls[1].Offset);
    }

    [Fact]
    public async Task Waits_double_from_two_seconds_up_to_five_minutes()
    {
        int[] expected = [2, 4, 8, 16, 32, 64, 128, 256, 300, 300];
        _telegram.FailsTimes(expected.Length, NoNetwork);

        Run();
        for (var i = 0; i < expected.Length; i++)
        {
            await _telegram.WaitForPollsAsync(i + 1);
            await NoPollUntilAsync(TimeSpan.FromSeconds(expected[i]), next: i + 2, timers: i + 1);
        }
    }

    [Fact]
    public async Task An_outage_is_logged_when_it_starts_and_when_it_ends_with_its_length_and_reported()
    {
        _telegram.FailsTimes(2, NoNetwork).Returns(UpdateBatch.Empty);

        Run();
        await AdvanceAsync(timers: 1, TimeSpan.FromSeconds(2));
        await AdvanceAsync(timers: 2, TimeSpan.FromSeconds(4));
        await _telegram.WaitForPollsAsync(4);

        Assert.Equal(
            [
                (LogKind.Info, "telegram: offline (getUpdates: No such host is known.); retrying in 2 s"),
                (LogKind.Info, "telegram: online again after 6 s (2 failed polls)"),
            ],
            _log.Lines);
        Assert.Equal([new Outage(Start, Start.AddSeconds(6), 2, NoNetwork.Message)], _reported);
    }

    [Fact]
    public async Task After_a_reconnection_the_waits_start_again_from_two_seconds()
    {
        _telegram.FailsTimes(3, NoNetwork).Returns(UpdateBatch.Empty).Fails(NoNetwork);

        Run();
        await AdvanceAsync(timers: 1, TimeSpan.FromSeconds(2));
        await AdvanceAsync(timers: 2, TimeSpan.FromSeconds(4));
        await AdvanceAsync(timers: 3, TimeSpan.FromSeconds(8));
        await _telegram.WaitForPollsAsync(5);

        await NoPollUntilAsync(TimeSpan.FromSeconds(2), next: 6, timers: 4);
    }

    [Fact]
    public async Task Retry_after_from_telegram_is_respected()
    {
        _telegram.Fails(new TelegramApiException(429, "Too Many Requests: retry after 35", TimeSpan.FromSeconds(35)));

        Run();

        await NoPollUntilAsync(TimeSpan.FromSeconds(35), next: 2, timers: 1);
    }

    [Fact]
    public async Task A_rejected_token_is_an_error_and_waits_the_maximum()
    {
        _telegram.Fails(new TelegramApiException(401, "Unauthorized", null));

        Run();
        await _clock.WaitForTimersAsync(1);

        Assert.Equal(LogKind.Error, Assert.Single(_log.Lines).Kind);
        await NoPollUntilAsync(TelegramPoller.MaxRetry, next: 2, timers: 1);
    }

    [Fact]
    public async Task An_unexpected_poll_failure_is_an_error_and_does_not_stop_the_loop()
    {
        _telegram.Fails(new InvalidOperationException("The requested key was not present."));

        Run();
        await _clock.WaitForTimersAsync(1);

        Assert.Contains(_log.Lines, l => l.Kind == LogKind.Error && l.Message.Contains("InvalidOperationException"));
        await NoPollUntilAsync(TelegramPoller.MaxRetry, next: 2, timers: 1);
    }

    [Fact]
    public async Task A_message_whose_answer_fails_for_lack_of_network_comes_again()
    {
        var failOnce = true;
        _onMessage = message =>
        {
            if (failOnce)
            {
                failOnce = false;
                throw new TelegramUnavailableException("sendMessage: No such host is known.");
            }

            return Task.CompletedTask;
        };
        _telegram.Returns(Batch(11, 10)).Returns(Batch(11, 10));

        Run();
        await AdvanceAsync(timers: 1, TimeSpan.FromSeconds(2));
        await _telegram.WaitForPollsAsync(3);

        Assert.Null(_telegram.Polls[1].Offset);
        Assert.Equal([10, 10], _handled);
        Assert.Equal(11, _telegram.Polls[2].Offset);
    }

    [Fact]
    public async Task A_message_whose_answer_fails_by_a_fault_is_skipped_and_logged()
    {
        _onMessage = message => message.UpdateId == 10 ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        _telegram.Returns(Batch(12, 10, 11));

        Run();
        await _telegram.WaitForPollsAsync(2);

        Assert.Equal([10, 11], _handled);
        Assert.Equal(12, _telegram.Polls[1].Offset);
        Assert.Equal((LogKind.Error, "telegram: update 10 skipped: InvalidOperationException: boom"), Assert.Single(_log.Lines));
    }

    [Fact]
    public async Task An_outage_report_that_cannot_be_sent_is_sent_after_the_next_outage()
    {
        var failOnce = true;
        _onReconnected = _ =>
        {
            if (failOnce)
            {
                failOnce = false;
                throw new TelegramUnavailableException("sendMessage: No such host is known.");
            }

            return Task.CompletedTask;
        };
        _telegram.Fails(NoNetwork).Returns(UpdateBatch.Empty).Returns(UpdateBatch.Empty);

        Run();
        await AdvanceAsync(timers: 1, TimeSpan.FromSeconds(2));
        await AdvanceAsync(timers: 2, TimeSpan.FromSeconds(2));
        await _telegram.WaitForPollsAsync(4);

        Assert.Equal(2, _reported.Count);
        Assert.Equal(Start, _reported[0].Start);
        Assert.Equal(Start.AddSeconds(2), _reported[1].Start);
    }

    [Fact]
    public async Task An_outage_report_that_fails_by_a_fault_is_dropped_not_retried_forever()
    {
        var attempts = 0;
        _onReconnected = _ =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("boom");
        };
        _telegram.Fails(NoNetwork).Returns(UpdateBatch.Empty).Returns(UpdateBatch.Empty);

        Run();
        await AdvanceAsync(timers: 1, TimeSpan.FromSeconds(2));
        await _telegram.WaitForPollsAsync(4);

        Assert.Equal(1, attempts);
        Assert.Contains(_log.Lines, l => l.Message == "telegram: outage report dropped: InvalidOperationException: boom");
    }

    [Fact]
    public async Task Cancelling_ends_the_loop_quietly_while_polling_or_waiting()
    {
        _telegram.Fails(NoNetwork);

        Run();
        await _clock.WaitForTimersAsync(1);
        await _stop.CancelAsync();

        await _run!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(_run.IsCompletedSuccessfully);
    }

    [Fact]
    public void The_reconnection_text_says_how_long_and_since_when()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
        var outage = new Outage(Start, Start.AddMinutes(12), 9, "no network");

        Assert.Equal(
            "Vuelvo a tener conexión. Estuve 12 min sin poder hablar con Telegram, desde hoy a las 11:00.",
            BotTexts.Reconnected(outage, zone));
    }

    private void Run()
    {
        var poller = new TelegramPoller(_telegram, _clock, _log);
        _run = poller.RunAsync(
            async (message, _) =>
            {
                lock (_handled) { _handled.Add(message.UpdateId); }
                await _onMessage(message);
            },
            async (outage, _) =>
            {
                await _onReconnected(outage);
                lock (_reported) { _reported.Add(outage); }
            },
            _stop.Token);
    }

    /// <summary>Once the loop waits on its <paramref name="timers"/>-th timer, moves the clock by <paramref name="by"/>.</summary>
    private async Task AdvanceAsync(int timers, TimeSpan by)
    {
        await _clock.WaitForTimersAsync(timers);
        _clock.Advance(by);
    }

    /// <summary>Checks that poll number <paramref name="next"/> comes exactly <paramref name="wait"/> after the loop starts waiting.</summary>
    private async Task NoPollUntilAsync(TimeSpan wait, int next, int timers)
    {
        await _clock.WaitForTimersAsync(timers);
        _clock.Advance(wait - TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        Assert.Equal(next - 1, _telegram.Polls.Count);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await _telegram.WaitForPollsAsync(next);
    }

    private static UpdateBatch Batch(long nextOffset, params long[] updateIds) =>
        new([.. updateIds.Select(id => new IncomingMessage(id, Owner, "/foto", Start))], nextOffset);
}
