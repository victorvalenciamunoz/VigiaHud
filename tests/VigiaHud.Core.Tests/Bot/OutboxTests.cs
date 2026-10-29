using VigiaHud.Core.Bot;
using VigiaHud.Core.Logging;
using VigiaHud.Core.Telegram;
using VigiaHud.Core.Tests.Support;

namespace VigiaHud.Core.Tests.Bot;

public class OutboxTests
{
    private const long Owner = 111_222_333;
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 10, 0, 0, TimeSpan.Zero); // 11:00 local

    private readonly SteppingClock _clock = new(Start);
    private readonly FakeTelegramClient _telegram = new();
    private readonly MemoryLog _log = new();

    [Fact]
    public async Task Sends_what_is_pending_in_order_and_empties()
    {
        var outbox = NewOutbox();
        outbox.Enqueue("uno");
        outbox.Enqueue("dos");

        var sent = await outbox.FlushAsync(CancellationToken.None);

        Assert.Equal(2, sent);
        Assert.Equal([(Owner, "uno"), (Owner, "dos")], _telegram.Sent);
        Assert.Equal(0, outbox.Count);
    }

    [Fact]
    public async Task Without_network_it_stops_and_keeps_the_rest_in_order_for_next_time()
    {
        var outbox = NewOutbox();
        outbox.Enqueue("uno");
        outbox.Enqueue("dos");
        _telegram.FailsToSend(new TelegramUnavailableException("sendMessage: No such host is known."));

        Assert.Equal(0, await outbox.FlushAsync(CancellationToken.None));
        Assert.Equal(2, outbox.Count);

        Assert.Equal(2, await outbox.FlushAsync(CancellationToken.None));
        Assert.Equal(["uno", "dos"], _telegram.Sent.Select(s => s.Text));
    }

    [Fact]
    public async Task A_message_telegram_refuses_for_good_is_dropped_and_logged_not_blocking_the_rest()
    {
        var outbox = NewOutbox();
        outbox.Enqueue("demasiado largo");
        outbox.Enqueue("dos");
        _telegram.FailsToSend(new TelegramApiException(400, "Bad Request: message is too long", null));

        Assert.Equal(1, await outbox.FlushAsync(CancellationToken.None));

        Assert.Equal(["dos"], _telegram.Sent.Select(s => s.Text));
        Assert.Contains(_log.Lines, l => l.Kind == LogKind.Error && l.Message.Contains("message is too long"));
    }

    [Fact]
    public async Task A_message_that_leaves_late_says_when_it_was_written()
    {
        var outbox = NewOutbox();
        outbox.Enqueue("Se ha ido la luz.");
        _clock.Advance(TimeSpan.FromMinutes(95));

        await outbox.FlushAsync(CancellationToken.None);

        Assert.Equal("(Con retraso: es de hoy a las 11:00.)\nSe ha ido la luz.", Assert.Single(_telegram.Sent).Text);
    }

    [Fact]
    public async Task A_message_a_minute_old_is_not_late()
    {
        var outbox = NewOutbox();
        outbox.Enqueue("Sigo vivo.");
        _clock.Advance(TimeSpan.FromMinutes(1));

        await outbox.FlushAsync(CancellationToken.None);

        Assert.Equal("Sigo vivo.", Assert.Single(_telegram.Sent).Text);
    }

    [Fact]
    public async Task When_full_the_oldest_gives_way_with_a_line_in_the_log()
    {
        var outbox = NewOutbox(capacity: 2);
        outbox.Enqueue("uno");
        outbox.Enqueue("dos");
        outbox.Enqueue("tres");

        await outbox.FlushAsync(CancellationToken.None);

        Assert.Equal(["dos", "tres"], _telegram.Sent.Select(s => s.Text));
        Assert.Contains(_log.Lines, l => l.Kind == LogKind.Error && l.Message.StartsWith("telegram: outbox full", StringComparison.Ordinal));
    }

    private Outbox NewOutbox(int capacity = Outbox.DefaultCapacity) => new(_telegram, Owner, _clock, _log, Zone, capacity);
}
