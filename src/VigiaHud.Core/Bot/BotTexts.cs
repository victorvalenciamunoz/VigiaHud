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

        var battery = device.BatteryPercent is { } percent
            ? string.Create(Spanish, $"Batería {percent} %")
            : "Batería desconocida";
        var charger = device.IsPluggedIn ? "enchufado" : "sin cargador";
        var temperature = device.BatteryTemperatureCelsius is { } celsius
            ? string.Create(Spanish, $", {celsius:0.0} °C")
            : string.Empty;

        var lines = new List<string>
        {
            $"Vigilando desde hace {Duration(watching)}.",
            $"{battery}, {charger}{temperature}.",
            lastDailyHeartbeat is { } beat
                ? $"Último latido diario: {Moment(beat, now, zone)}."
                : "Último latido diario: todavía ninguno.",
        };

        if (device.IsBatteryOptimizationIgnored == false)
        {
            lines.Add("Ojo: Android puede dormir la vigilancia. Abre la app y pulsa EXCLUIR DE BATERÍA.");
        }

        return string.Join('\n', lines);
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
