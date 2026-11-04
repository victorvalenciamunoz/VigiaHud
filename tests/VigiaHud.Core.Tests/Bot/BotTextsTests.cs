using VigiaHud.Core.Bot;
using VigiaHud.Core.Device;
using VigiaHud.Core.Recording;
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

        var text = BotTexts.DailyHeartbeat(new DeviceStatus(100, true, 27.5, true), TimeSpan.FromHours(76), [], [], plan, Zone);

        Assert.Equal(
            "Sigo vivo.\n" +
            "Vigilando desde hace 3 d 4 h.\n" +
            "Batería 100 %, enchufado, 27,5 °C.\n" +
            "Últimas 24 h: sin cortes de conexión ni de corriente.",
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

        var text = BotTexts.DailyHeartbeat(new DeviceStatus(70, false, 24.0, false), TimeSpan.FromMinutes(90), outages, [], plan, Zone);

        Assert.Equal(
            "Sigo vivo (con retraso: a las 09:00 no pude).\n" +
            "Vigilando desde hace 1 h 30 min.\n" +
            "Batería 70 %, sin cargador, 24,0 °C.\n" +
            "Últimas 24 h: 2 cortes de conexión, 14 min en total; sin cortes de corriente.\n" +
            BotTexts.DozeWarning,
            text);
    }

    [Fact]
    public void One_outage_is_told_with_its_length()
    {
        Outage[] outages = [new(Now.AddHours(-1), Now.AddHours(-1).AddMinutes(12), 7, "x")];

        var text = BotTexts.DailyHeartbeat(new DeviceStatus(90, true, 25.0, true), TimeSpan.FromHours(30), outages, [], new HeartbeatPlan(Now, false, Now), Zone);

        Assert.Contains("Últimas 24 h: 1 corte de conexión, de 12 min; sin cortes de corriente.", text);
    }

    [Fact]
    public void Short_power_cuts_appear_in_the_summary_too()
    {
        PowerCut[] cuts = [new(Now.AddHours(-3), Now.AddHours(-3).AddMinutes(4))];

        var text = BotTexts.DailyHeartbeat(new DeviceStatus(95, true, 25.0, true), TimeSpan.FromHours(30), [], cuts, new HeartbeatPlan(Now, false, Now), Zone);

        Assert.Contains("Últimas 24 h: sin cortes de conexión; 1 corte de corriente, de 4 min.", text);
    }

    [Fact]
    public void Power_lost_says_since_when_the_battery_and_the_rough_margin()
    {
        var since = new DateTimeOffset(2026, 10, 29, 2, 12, 0, TimeSpan.Zero); // 03:12 local

        Assert.Equal(
            "Sin corriente desde hoy a las 03:12: se ha desenchufado el cargador o se ha ido la luz. Batería al 84 %, unas 16 h de margen.",
            BotTexts.PowerLost(new PowerNotice.Lost(since, 84), Now, Zone));
    }

    [Fact]
    public void Power_lost_without_a_battery_reading_leaves_the_margin_out()
    {
        var since = new DateTimeOffset(2026, 10, 29, 2, 12, 0, TimeSpan.Zero);

        Assert.EndsWith("se ha ido la luz.", BotTexts.PowerLost(new PowerNotice.Lost(since, null), Now, Zone));
    }

    [Fact]
    public void Power_back_says_how_long_and_the_battery() =>
        Assert.Equal(
            "Vuelve la corriente. Estuvo 1 h 20 min sin cargador; batería al 72 %.",
            BotTexts.PowerBack(new PowerNotice.Back(new PowerCut(Now, Now.AddMinutes(80)), 72)));

    [Theory]
    [InlineData(15, "Batería al 15 % y sin cargador: me apagaré en unas 3 h.")]
    [InlineData(5, "Batería al 5 % y sin cargador: me apagaré en 1 h aproximadamente.")]
    [InlineData(2, "Batería al 2 % y sin cargador: me apagaré en menos de 1 h.")]
    public void Low_battery_says_roughly_when_the_phone_will_die(int percent, string expected) =>
        Assert.Equal(expected, BotTexts.LowBattery(new PowerNotice.LowBattery(percent)));

    [Fact]
    public void Photo_caption_is_the_local_time() =>
        Assert.Equal("Foto de las 12:51.", BotTexts.PhotoCaption(new DateTimeOffset(2026, 10, 29, 11, 51, 30, TimeSpan.Zero), Zone));

    [Fact]
    public void Help_lists_the_commands()
    {
        Assert.Contains("/foto", BotTexts.Help);
        Assert.Contains("/estado", BotTexts.Help);
        Assert.Contains("/ayuda", BotTexts.Help);
        Assert.Contains("/grabar", BotTexts.Help);
    }

    [Theory]
    [InlineData(10, "10 s")]
    [InlineData(90, "1 min 30 s")]
    [InlineData(120, "2 min")]
    [InlineData(5400, "1 h 30 min")]
    [InlineData(8 * 3600, "8 h")]
    public void Recording_started_says_the_exact_length(int seconds, string length) =>
        Assert.Equal(
            $"Grabo «salida-1» durante {length}, una foto por segundo. Te aviso al terminar.",
            BotTexts.RecordingStarted(Recording("salida-1", TimeSpan.FromSeconds(seconds), 0, 0, null)));

    [Fact]
    public void Recording_busy_says_how_long_is_left() =>
        Assert.Equal(
            "Ya estoy grabando «salida-1»: quedan 1 min 30 s. Para cortarla: /grabar parar.",
            BotTexts.RecordingBusy(Recording("salida-1", TimeSpan.FromMinutes(2), 0, 0, null), Now.AddSeconds(30)));

    [Fact]
    public void Recording_without_space_says_how_much_is_left() =>
        Assert.Equal(
            "No grabo: quedan 1,2 GB libres en el móvil y tengo que dejar al menos 1,5 GB.",
            BotTexts.RecordingNoSpace(1_234_000_000));

    [Theory]
    [InlineData(RecordingEnd.Completed, "Grabación «salida-1» terminada: 120 fotos, 3,5 MB.")]
    [InlineData(RecordingEnd.Stopped, "Grabación «salida-1» cortada: 120 fotos, 3,5 MB.")]
    [InlineData(RecordingEnd.LowSpace, "Grabación «salida-1» parada por falta de espacio: 120 fotos, 3,5 MB. Hay que pasarla al PC.")]
    [InlineData(RecordingEnd.Failed, "Grabación «salida-1» parada por un error: 120 fotos, 3,5 MB. Mira el registro.")]
    [InlineData(RecordingEnd.Interrupted, "Grabación «salida-1» interrumpida al parar la vigilancia: 120 fotos, 3,5 MB.")]
    public void Recording_finished_says_how_it_ended_and_its_size(RecordingEnd end, string expected) =>
        Assert.Equal(expected, BotTexts.RecordingFinished(Recording("salida-1", TimeSpan.FromMinutes(2), 120, 3_520_000, end)));

    [Theory]
    [InlineData(1, 350_000, "1 foto, 350 KB")]
    [InlineData(43_200, 1_400_000_000, "43200 fotos, 1,4 GB")]
    public void Recording_sizes_read_in_the_right_unit(int frames, long bytes, string expected) =>
        Assert.Contains(expected, BotTexts.RecordingFinished(Recording("x", TimeSpan.FromHours(1), frames, bytes, RecordingEnd.Completed)));

    [Fact]
    public void A_recording_without_frames_points_to_the_log() =>
        Assert.Equal(
            "Grabación «salida-1» terminada sin fotos: la cámara no las daba. Mira el registro.",
            BotTexts.RecordingFinished(Recording("salida-1", TimeSpan.FromMinutes(2), 0, 0, RecordingEnd.Completed)));

    [Fact]
    public void Every_recording_problem_has_its_text()
    {
        foreach (var problem in Enum.GetValues<RecordingProblem>())
        {
            Assert.False(string.IsNullOrWhiteSpace(BotTexts.RecordingProblem(problem)));
        }
    }

    private static RecordingState Recording(string name, TimeSpan duration, int frames, long bytes, RecordingEnd? end) =>
        new(name, $"/data/{name}.vseq", Now, duration, frames, bytes, end);
}
