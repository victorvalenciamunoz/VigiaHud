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
        "/grabar: graba una escena de prueba para ajustar el detector.\n" +
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

    public const string RecordingUsage =
        "Para grabar una escena de prueba: /grabar 2 salida-1 (2 minutos, con ese nombre) o /grabar 8h casa-vacia. " +
        "Para cortarla: /grabar parar. La vigilancia sigue mientras grabo.";

    public const string NotRecording = "No estoy grabando nada.";

    public const string RecordingUnavailable = "Ahora no puedo grabar: la vigilancia no está en marcha.";

    public static string RecordingProblem(Recording.RecordingProblem problem) => problem switch
    {
        Recording.RecordingProblem.BadDuration => "No entiendo la duración. Ejemplos: /grabar 2 (minutos), /grabar 90s, /grabar 8h.",
        Recording.RecordingProblem.TooShort => "Muy corta: graba al menos 10 s.",
        Recording.RecordingProblem.TooLong => "Muy larga: como mucho 12 h.",
        Recording.RecordingProblem.BadName => "El nombre va en una palabra, con letras sin tilde, números y guiones, hasta 40: salida-1.",
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, null),
    };

    /// <summary>"Grabo «salida-1» durante 2 min, una foto por segundo. Te aviso al terminar."</summary>
    public static string RecordingStarted(Recording.RecordingState recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return $"Grabo «{recording.Name}» durante {Length(recording.Duration)}, una foto por segundo. Te aviso al terminar.";
    }

    public static string RecordingBusy(Recording.RecordingState recording, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(recording);
        var left = recording.EndsAt - now;
        return $"Ya estoy grabando «{recording.Name}»: quedan {Length(TimeSpan.FromSeconds(Math.Ceiling(Math.Max(left.TotalSeconds, 0))))}. Para cortarla: /grabar parar.";
    }

    public static string RecordingNoSpace(long freeBytes) =>
        $"No grabo: quedan {Size(freeBytes)} libres en el móvil y tengo que dejar al menos {Size(Recording.Recorder.MinFreeBytes)}.";

    /// <summary>How a recording ended, with its frames and size, so the owner knows the scene can end.</summary>
    public static string RecordingFinished(Recording.RecordingState recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (recording.Frames == 0)
        {
            return $"Grabación «{recording.Name}» terminada sin fotos: la cámara no las daba. Mira el registro.";
        }

        var photos = recording.Frames == 1 ? "1 foto" : string.Create(Spanish, $"{recording.Frames} fotos");
        var what = $"{photos}, {Size(recording.Bytes)}";
        return recording.End switch
        {
            Recording.RecordingEnd.Stopped => $"Grabación «{recording.Name}» cortada: {what}.",
            Recording.RecordingEnd.LowSpace => $"Grabación «{recording.Name}» parada por falta de espacio: {what}. Hay que pasarla al PC.",
            Recording.RecordingEnd.Failed => $"Grabación «{recording.Name}» parada por un error: {what}. Mira el registro.",
            Recording.RecordingEnd.Interrupted => $"Grabación «{recording.Name}» interrumpida al parar la vigilancia: {what}.",
            _ => $"Grabación «{recording.Name}» terminada: {what}.",
        };
    }

    /// <summary>"45 s", "2 min", "1 min 30 s", "8 h", "1 h 30 min": exact, since the owner chose it.</summary>
    private static string Length(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
        {
            return string.Create(Spanish, $"{(int)span.TotalSeconds} s");
        }

        if (span < TimeSpan.FromHours(1))
        {
            return span.Seconds == 0
                ? string.Create(Spanish, $"{(int)span.TotalMinutes} min")
                : string.Create(Spanish, $"{(int)span.TotalMinutes} min {span.Seconds} s");
        }

        return span.Minutes == 0
            ? string.Create(Spanish, $"{(int)span.TotalHours} h")
            : string.Create(Spanish, $"{(int)span.TotalHours} h {span.Minutes} min");
    }

    /// <summary>"350 KB", "3,5 MB", "1,4 GB" (decimal units, like the phone's settings).</summary>
    private static string Size(long bytes) => bytes switch
    {
        < 1_000_000 => string.Create(Spanish, $"{Math.Max(bytes / 1000, 1)} KB"),
        < 1_000_000_000 => string.Create(Spanish, $"{bytes / 1e6:0.0} MB"),
        _ => string.Create(Spanish, $"{bytes / 1e9:0.0} GB"),
    };

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
