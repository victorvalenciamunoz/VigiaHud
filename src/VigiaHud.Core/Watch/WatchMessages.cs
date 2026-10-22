using System.Globalization;
using VigiaHud.Core.Device;

namespace VigiaHud.Core.Watch;

/// <summary>
/// The messages of the capture and heartbeat lines: key=value pairs, invariant culture, so the judge scripts parse
/// them with a split and nothing else.
/// </summary>
public static class WatchMessages
{
    public static string Capture(CaptureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return string.Create(CultureInfo.InvariantCulture,
            $"frames={report.Frames} failed={report.Failures} period={Math.Round(report.Period.TotalSeconds)}s");
    }

    public static string Heartbeat(DeviceStatus status, TimeSpan uptime)
    {
        ArgumentNullException.ThrowIfNull(status);
        var battery = status.BatteryPercent is { } percent ? string.Create(CultureInfo.InvariantCulture, $"{percent}%") : "?";
        var temperature = status.BatteryTemperatureCelsius is { } celsius ? string.Create(CultureInfo.InvariantCulture, $"{celsius:0.0}C") : "?";
        return $"uptime={FormatUptime(uptime)} battery={battery} plugged={(status.IsPluggedIn ? "yes" : "no")} temp={temperature}";
    }

    /// <summary>"6m", "1h02m", "2d01h03m": days only when there are days, hours only when there are hours.</summary>
    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero)
        {
            uptime = TimeSpan.Zero;
        }

        var minutes = (int)uptime.TotalMinutes;
        var days = minutes / (24 * 60);
        var hours = minutes / 60 % 24;
        var rest = minutes % 60;
        if (days > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{days}d{hours:00}h{rest:00}m");
        }

        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}h{rest:00}m")
            : string.Create(CultureInfo.InvariantCulture, $"{rest}m");
    }
}
