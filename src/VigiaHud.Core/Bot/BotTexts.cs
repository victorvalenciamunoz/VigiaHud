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
    /// The daily "sigo vivo" (PLAN, phase 2): time watching, battery, and what happened in the last 24 hours. A late
    /// one says so and which hour it was meant for (DECISIONS 019).
    /// </summary>
    public static string DailyHeartbeat(DeviceStatus device, TimeSpan watching, IReadOnlyCollection<Telegram.Outage> outagesLastDay, HeartbeatPlan plan, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(outagesLastDay);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(zone);

        var due = TimeZoneInfo.ConvertTime(plan.DueAt, zone);
        var lines = new List<string>
        {
            plan.Late ? string.Create(Spanish, $"Sigo vivo (con retraso: a las {due:HH:mm} no pude).") : "Sigo vivo.",
            $"Vigilando desde hace {Duration(watching)}.",
            Battery(device),
            outagesLastDay.Count switch
            {
                0 => "Últimas 24 h: sin cortes de conexión.",
                1 => $"Últimas 24 h: 1 corte de conexión, de {Duration(outagesLastDay.First().Duration)}.",
                var n => $"Últimas 24 h: {n} cortes de conexión, {Duration(TimeSpan.FromTicks(outagesLastDay.Sum(o => o.Duration.Ticks)))} en total.",
            },
        };
        AddDozeWarning(lines, device);
        return string.Join('\n', lines);
    }

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
