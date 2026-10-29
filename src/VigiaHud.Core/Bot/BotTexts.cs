using System.Globalization;
using VigiaHud.Core.Device;

namespace VigiaHud.Core.Bot;

/// <summary>
/// What the bot says, in Spanish, short (CLAUDE.md). Times in the phone's local zone, which the caller passes in:
/// nothing here names a place.
/// </summary>
public static class BotTexts
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public const string Help =
        "Soy VigíaHud. Esto es lo que sé hacer:\n" +
        "/foto: una foto de ahora.\n" +
        "/estado: batería, cargador, temperatura y tiempo vigilando.\n" +
        "/ayuda: esta lista.\n" +
        "Cada mañana te escribo para decirte que sigo vivo. Si un día no llega, algo falla.";

    public const string Unknown = "No te entiendo. Escribe /ayuda para ver lo que sé hacer.";

    /// <summary>
    /// The answer to <c>/estado</c>: time watching, battery and charger, temperature, the last daily heartbeat, and a
    /// warning if Android may put the watch to sleep (the battery-optimization exemption is lost on every reboot).
    /// </summary>
    public static string Status(DeviceStatus device, TimeSpan watching, DateTimeOffset? lastDailyHeartbeat, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(zone);

        var lines = new List<string>
        {
            $"Vigilando desde hace {Duration(watching)}.",
            Battery(device),
            lastDailyHeartbeat is { } beat
                ? $"Último latido diario: {Moment(beat, now, zone)}."
                : "Último latido diario: todavía ninguno.",
        };
        AddDozeWarning(lines, device);
        return string.Join('\n', lines);
    }

    public const string DozeWarning = "Ojo: Android puede dormir la vigilancia. Abre la app y pulsa EXCLUIR DE BATERÍA.";

    public const string CameraNotReady = "Ahora no puedo hacer la foto: la cámara no está lista. Prueba otra vez en un minuto.";

    /// <summary>
    /// The daily "sigo vivo" (PLAN, phase 2): time watching, battery, and the cuts of the last 24 hours, connection
    /// and power, short ones included. A late one says so and which hour it was meant for (DECISIONS 019).
    /// </summary>
    public static string DailyHeartbeat(
        DeviceStatus device,
        TimeSpan watching,
        IReadOnlyCollection<Telegram.Outage> outagesLastDay,
        IReadOnlyCollection<PowerCut> powerCutsLastDay,
        HeartbeatPlan plan,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(outagesLastDay);
        ArgumentNullException.ThrowIfNull(powerCutsLastDay);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(zone);

        var due = TimeZoneInfo.ConvertTime(plan.DueAt, zone);
        var connection = Cuts("conexión", [.. outagesLastDay.Select(o => o.Duration)]);
        var power = Cuts("corriente", [.. powerCutsLastDay.Select(c => c.Duration)]);
        var lines = new List<string>
        {
            plan.Late ? string.Create(Spanish, $"Sigo vivo (con retraso: a las {due:HH:mm} no pude).") : "Sigo vivo.",
            $"Vigilando desde hace {Duration(watching)}.",
            Battery(device),
            outagesLastDay.Count == 0 && powerCutsLastDay.Count == 0
                ? "Últimas 24 h: sin cortes de conexión ni de corriente."
                : $"Últimas 24 h: {connection}; {power}.",
        };
        AddDozeWarning(lines, device);
        return string.Join('\n', lines);
    }

    /// <summary>"Sin corriente desde hoy a las 03:12: …" with the battery and the rough margin left.</summary>
    public static string PowerLost(PowerNotice.Lost notice, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(zone);
        var battery = notice.BatteryPercent is { } percent
            ? $" Batería al {percent} %, {Margin(percent)} de margen."
            : string.Empty;
        return $"Sin corriente desde {Moment(notice.Since, now, zone)}: se ha desenchufado el cargador o se ha ido la luz.{battery}";
    }

    public static string PowerBack(PowerNotice.Back notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var battery = notice.BatteryPercent is { } percent ? $"; batería al {percent} %" : string.Empty;
        return $"Vuelve la corriente. Estuvo {Duration(notice.Cut.Duration)} sin cargador{battery}.";
    }

    public static string LowBattery(PowerNotice.LowBattery notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        return $"Batería al {notice.BatteryPercent} % y sin cargador: me apagaré en {Margin(notice.BatteryPercent)}.";
    }

    /// <summary>"unas 9 h", "1 h aproximadamente", "menos de 1 h": a rough figure said as one.</summary>
    private static string Margin(int batteryPercent) => PowerWatch.HoursLeft(batteryPercent) switch
    {
        0 => "menos de 1 h",
        1 => "1 h aproximadamente",
        var hours => $"unas {hours} h",
    };

    /// <summary>"sin cortes de corriente", "1 corte de corriente, de 4 min", "2 cortes de corriente, 1 h 10 min en total".</summary>
    private static string Cuts(string what, IReadOnlyList<TimeSpan> lengths) => lengths.Count switch
    {
        0 => $"sin cortes de {what}",
        1 => $"1 corte de {what}, de {Duration(lengths[0])}",
        var n => $"{n} cortes de {what}, {Duration(TimeSpan.FromTicks(lengths.Sum(l => l.Ticks)))} en total",
    };

    /// <summary>"Batería 87 %, enchufado, 31,2 °C."</summary>
    private static string Battery(DeviceStatus device)
    {
        var battery = device.BatteryPercent is { } percent
            ? string.Create(Spanish, $"Batería {percent} %")
            : "Batería desconocida";
        var charger = device.IsPluggedIn ? "enchufado" : "sin cargador";
        var temperature = device.BatteryTemperatureCelsius is { } celsius
            ? string.Create(Spanish, $", {celsius:0.0} °C")
            : string.Empty;
        return $"{battery}, {charger}{temperature}.";
    }

    private static void AddDozeWarning(List<string> lines, DeviceStatus device)
    {
        if (device.IsBatteryOptimizationIgnored == false)
        {
            lines.Add(DozeWarning);
        }
    }

    /// <summary>Said once the bot can talk again after an outage: how long it lasted and since when.</summary>
    public static string Reconnected(Telegram.Outage outage, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(outage);
        ArgumentNullException.ThrowIfNull(zone);
        return $"Vuelvo a tener conexión. Estuve {Duration(outage.Duration)} sin poder hablar con Telegram, desde {Moment(outage.Start, outage.End, zone)}.";
    }

    /// <summary>The caption of a photo taken at <paramref name="takenAt"/>: just the local time.</summary>
    public static string PhotoCaption(DateTimeOffset takenAt, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(takenAt, zone);
        return string.Create(Spanish, $"Foto de las {local:HH:mm}.");
    }

    /// <summary>"12 min", "3 h 05 min", "2 d 3 h": the two largest units that matter.</summary>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalDays >= 1)
        {
            return string.Create(Spanish, $"{(int)span.TotalDays} d {span.Hours} h");
        }

        return span.TotalHours >= 1
            ? string.Create(Spanish, $"{(int)span.TotalHours} h {span.Minutes:00} min")
            : string.Create(Spanish, $"{(int)span.TotalMinutes} min");
    }

    /// <summary>"hoy a las 09:00", "ayer a las 09:00", or "el 27/10 a las 09:00", in local time.</summary>
    public static string Moment(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var day = local.Date == today ? "hoy"
            : local.Date == today.AddDays(-1) ? "ayer"
            : string.Create(Spanish, $"el {local:dd/MM}");
        return string.Create(Spanish, $"{day} a las {local:HH:mm}");
    }
}
