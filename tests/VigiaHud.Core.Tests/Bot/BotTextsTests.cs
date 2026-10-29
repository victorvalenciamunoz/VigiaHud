using VigiaHud.Core.Bot;
using VigiaHud.Core.Device;
using Outage = VigiaHud.Core.Telegram.Outage;

namespace VigiaHud.Core.Tests.Bot;

public class BotTextsTests
{
    // A fixed zone one hour ahead of UTC with no daylight saving: the tests do not depend on the machine's zone.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 11, 0, 0, TimeSpan.Zero); // 12:00 local

    [Fact]
    public void Status_says_time_watching_battery_charger_temperature_and_last_heartbeat()
    {
        var device = new DeviceStatus(87, true, 31.24, true);
        var lastBeat = new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero); // 09:00 local

        var text = BotTexts.Status(device, TimeSpan.FromHours(51), lastBeat, Now, Zone);

        Assert.Equal(
            "Vigilando desde hace 2 d 3 h.\n" +
            "Batería 87 %, enchufado, 31,2 °C.\n" +
            "Último latido diario: hoy a las 09:00.",
            text);
    }

    [Fact]
    public void Status_without_charger_or_known_numbers_says_so()
    {
        var device = new DeviceStatus(null, false, null, null);

        var text = BotTexts.Status(device, TimeSpan.FromMinutes(12), null, Now, Zone);

        Assert.Equal(
            "Vigilando desde hace 12 min.\n" +
            "Batería desconocida, sin cargador.\n" +
            "Último latido diario: todavía ninguno.",
            text);
    }

    [Fact]
    public void Status_warns_when_android_may_put_the_watch_to_sleep()
    {
        var device = new DeviceStatus(50, true, 30, false);

        var text = BotTexts.Status(device, TimeSpan.FromHours(1), null, Now, Zone);

        Assert.EndsWith("Abre la app y pulsa EXCLUIR DE BATERÍA.", text);
    }

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(59, "59 min")]
    [InlineData(60, "1 h 00 min")]
    [InlineData(185, "3 h 05 min")]
    [InlineData(1439, "23 h 59 min")]
    [InlineData(1440, "1 d 0 h")]
    [InlineData(3060, "2 d 3 h")]
    [InlineData(-5, "0 min")]
    public void Duration_uses_the_two_largest_units(int minutes, string expected) =>
        Assert.Equal(expected, BotTexts.Duration(TimeSpan.FromMinutes(minutes)));

    [Theory]
    [InlineData(2026, 10, 29, 8, 0, "hoy a las 09:00")]
    [InlineData(2026, 10, 28, 8, 0, "ayer a las 09:00")]
    [InlineData(2026, 10, 27, 8, 0, "el 27/10 a las 09:00")]
    [InlineData(2026, 10, 28, 23, 30, "hoy a las 00:30")]
    public void Moment_is_in_local_time_with_today_and_yesterday(int year, int month, int day, int hour, int minute, string expected)
    {
        var at = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);

        Assert.Equal(expected, BotTexts.Moment(at, Now, Zone));
    }

    [Fact]
    public void The_daily_heartbeat_says_alive_watching_battery_and_no_outages()
    {
        var plan = new HeartbeatPlan(Now, false, Now);

        var text = BotTexts.DailyHeartbeat(new DeviceStatus(100, true, 27.5, true), TimeSpan.FromHours(76), [], plan, Zone);

        Assert.Equal(
            "Sigo vivo.\n" +
            "Vigilando desde hace 3 d 4 h.\n" +
            "Batería 100 %, enchufado, 27,5 °C.\n" +
            "Últimas 24 h: sin cortes de conexión.",
            text);
    }

    [Fact]
    public void A_late_heartbeat_says_which_hour_it_missed_and_counts_the_outages()
    {
        var due = new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero); // 09:00 local
        var plan = new HeartbeatPlan(Now, true, due);
        Outage[] outages =
        [
            new(Now.AddHours(-5), Now.AddHours(-5).AddMinutes(4), 6, "x"),
            new(Now.AddHours(-2), Now.AddHours(-2).AddMinutes(10), 9, "x"),
        ];

        var text = BotTexts.DailyHeartbeat(new DeviceStatus(70, false, 24.0, false), TimeSpan.FromMinutes(90), outages, plan, Zone);

        Assert.Equal(
            "Sigo vivo (con retraso: a las 09:00 no pude).\n" +
            "Vigilando desde hace 1 h 30 min.\n" +
            "Batería 70 %, sin cargador, 24,0 °C.\n" +
            "Últimas 24 h: 2 cortes de conexión, 14 min en total.\n" +
            BotTexts.DozeWarning,
            text);
    }

    [Fact]
    public void One_outage_is_told_with_its_length()
    {
        Outage[] outages = [new(Now.AddHours(-1), Now.AddHours(-1).AddMinutes(12), 7, "x")];

        var text = BotTexts.DailyHeartbeat(new DeviceStatus(90, true, 25.0, true), TimeSpan.FromHours(30), outages, new HeartbeatPlan(Now, false, Now), Zone);

        Assert.Contains("Últimas 24 h: 1 corte de conexión, de 12 min.", text);
    }

    [Fact]
    public void Photo_caption_is_the_local_time() =>
        Assert.Equal("Foto de las 12:51.", BotTexts.PhotoCaption(new DateTimeOffset(2026, 10, 29, 11, 51, 30, TimeSpan.Zero), Zone));

    [Fact]
    public void Help_lists_the_three_commands()
    {
        Assert.Contains("/foto", BotTexts.Help);
        Assert.Contains("/estado", BotTexts.Help);
        Assert.Contains("/ayuda", BotTexts.Help);
    }
}
