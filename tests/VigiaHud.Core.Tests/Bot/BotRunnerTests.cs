using VigiaHud.Core.Bot;
using VigiaHud.Core.Device;
using VigiaHud.Core.Logging;
using VigiaHud.Core.Telegram;
using VigiaHud.Core.Tests.Support;

namespace VigiaHud.Core.Tests.Bot;

/// <summary>
/// The whole bot with a scripted Telegram: commands come in through polls and answers go out; the daily heartbeat
/// comes from the clock loop, the only one that waits on the clock while Telegram is quiet.
/// </summary>
public sealed class BotRunnerTests : IAsyncDisposable
{
    private const long Owner = 111_222_333;
    private const long Stranger = 999_888_777;
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");

    // 09:00 local in the test zone is 08:00 UTC.
    private static readonly DateTimeOffset NineToday = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeTelegramClient _telegram = new();
    private readonly MemoryLog _log = new();
    private readonly FakeEnvironment _environment = new();
    private readonly FakeMemory _memory = new();
    private readonly CancellationTokenSource _stop = new();
    private SteppingClock _clock = new(NineToday.AddHours(4));
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
    public async Task Status_is_answered_with_the_device_and_the_last_heartbeat()
    {
        _memory.LastDailyHeartbeat = NineToday;
        _telegram.Returns(Messages((Owner, "/estado")));

        Run();
        await SentAsync(1);

        Assert.Equal(
            "Vigilando desde hace 2 d 3 h.\nBatería 87 %, enchufado, 31,2 °C.\nÚltimo latido diario: hoy a las 09:00.",
            _telegram.Sent[0].Text);
    }

    [Fact]
    public async Task Photo_is_answered_with_a_picture_and_its_local_time()
    {
        _memory.LastDailyHeartbeat = NineToday;
        _telegram.Returns(Messages((Owner, "/foto")));

        Run();
        await SentAsync(1);

        Assert.Equal((Owner, "[foto] Foto de las 13:00."), _telegram.Sent[0]);
    }

    [Fact]
    public async Task Photo_without_a_ready_camera_says_so()
    {
        _memory.LastDailyHeartbeat = NineToday;
        _environment.Photo = null;
        _telegram.Returns(Messages((Owner, "/foto")));

        Run();
        await SentAsync(1);

        Assert.Equal(BotTexts.CameraNotReady, _telegram.Sent[0].Text);
    }

    [Fact]
    public async Task A_stranger_gets_nothing_and_leaves_a_line_in_the_log()
    {
        _memory.LastDailyHeartbeat = NineToday;
        _telegram.Returns(Messages((Stranger, "/foto"), (Owner, "/ayuda")));

        Run();
        await SentAsync(1);

        Assert.Equal([(Owner, BotTexts.Help)], _telegram.Sent);
        Assert.Contains(_log.Lines, l => l.Message.Contains("ignored message from chat 999888777"));
    }

    [Fact]
    public async Task The_daily_heartbeat_goes_out_at_nine_and_is_remembered()
    {
        _clock = new SteppingClock(NineToday.AddSeconds(-30));
        _memory.LastDailyHeartbeat = NineToday.AddDays(-1);

        Run();
        await _clock.WaitForTimersAsync(1);
        Assert.Empty(_telegram.Sent);
        _clock.Advance(TimeSpan.FromSeconds(30));
        await SentAsync(1);

        Assert.StartsWith("Sigo vivo.\nVigilando desde hace 2 d 3 h.", _telegram.Sent[0].Text);
        Assert.Equal(NineToday, _memory.LastDailyHeartbeat);
        Assert.Contains(_log.Lines, l => l.Message == "telegram: daily heartbeat written");
    }

    [Fact]
    public async Task A_missed_heartbeat_goes_out_on_start_marked_late()
    {
        _clock = new SteppingClock(NineToday.AddMinutes(90));
        _memory.LastDailyHeartbeat = NineToday.AddDays(-2);

        Run();
        await SentAsync(1);

        Assert.StartsWith("Sigo vivo (con retraso: a las 09:00 no pude).", _telegram.Sent[0].Text);
        Assert.Contains(_log.Lines, l => l.Message == "telegram: daily heartbeat written (late)");
    }

    [Fact]
    public async Task A_heartbeat_without_network_waits_in_the_outbox_and_goes_out_later_saying_so()
    {
        _clock = new SteppingClock(NineToday);
        _memory.LastDailyHeartbeat = NineToday.AddDays(-1);
        // Three minutes without network: the flushes at 09:00, 09:01 and 09:02 fail, the one at 09:03 goes out.
        for (var i = 0; i < 3; i++)
        {
            _telegram.FailsToSend(new TelegramUnavailableException("sendMessage: No such host is known."));
        }

        Run();
        await _clock.WaitForTimersAsync(1);
        Assert.Empty(_telegram.Sent);
        for (var minute = 1; minute <= 3; minute++)
        {
            await _clock.WaitForTimersAsync(minute);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        await SentAsync(1);
        Assert.StartsWith("(Con retraso: es de hoy a las 09:00.)\nSigo vivo.", _telegram.Sent[0].Text);
    }

    [Fact]
    public async Task Ten_minutes_without_charger_bring_the_power_notice_with_the_margin()
    {
        _memory.LastDailyHeartbeat = NineToday;
        _environment.Device = new DeviceStatus(84, false, 25.0, true);

        Run();
        for (var minute = 1; minute <= 10; minute++)
        {
            await _clock.WaitForTimersAsync(minute);
            Assert.Empty(_telegram.Sent);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        await SentAsync(1);
        Assert.Equal(
            "Sin corriente desde hoy a las 13:00: se ha desenchufado el cargador o se ha ido la luz. Batería al 84 %, unas 9 h de margen.",
            _telegram.Sent[0].Text);
        Assert.Contains(_log.Lines, l => l.Message.StartsWith("power: unplugged since", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_lost_exemption_is_told_as_soon_as_the_bot_starts()
    {
        _memory.LastDailyHeartbeat = NineToday;
        _environment.Device = new DeviceStatus(87, true, 31.2, false);

        Run();
        await SentAsync(1);

        Assert.Equal(BotTexts.DozeWarning, _telegram.Sent[0].Text);
        Assert.Contains(_log.Lines, l => l.Message == "power: battery optimization exemption lost");
    }

    [Theory]
    [InlineData(299, false)]
    [InlineData(300, true)]
    [InlineData(600, true)]
    public void Only_outages_of_five_minutes_or_more_get_a_message(int seconds, bool expected)
    {
        var outage = new Outage(NineToday, NineToday.AddSeconds(seconds), 3, "no network");

        Assert.Equal(expected, BotRunner.IsWorthNotifying(outage));
    }

    private void Run()
    {
        var runner = new BotRunner(_telegram, Owner, _environment, _memory, _clock, _log);
        _run = runner.RunAsync(_stop.Token);
    }

    private async Task SentAsync(int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (_telegram.Sent.Count < count)
        {
            await Task.Delay(10, deadline.Token);
        }
    }

    private static UpdateBatch Messages(params (long Chat, string Text)[] messages) =>
        new([.. messages.Select((m, i) => new IncomingMessage(100 + i, m.Chat, m.Text, NineToday))], 100 + messages.Length);

    private sealed class FakeEnvironment : IBotEnvironment
    {
        public TimeZoneInfo Zone => BotRunnerTests.Zone;

        public TimeSpan WatchingFor => TimeSpan.FromHours(51);

        public byte[]? Photo { get; set; } = [0xFF, 0xD8, 0xFF, 0xD9];

        public DeviceStatus Device { get; set; } = new(87, true, 31.2, true);

        public DeviceStatus ReadDevice() => Device;

        public Task<byte[]?> TakePhotoAsync(CancellationToken cancellationToken) => Task.FromResult(Photo);
    }

    private sealed class FakeMemory : IBotMemory
    {
        public DateTimeOffset? LastDailyHeartbeat { get; set; }
    }
}
