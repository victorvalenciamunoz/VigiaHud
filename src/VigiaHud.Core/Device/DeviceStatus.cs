namespace VigiaHud.Core.Device;

/// <summary>What the phone says about itself: the numbers of the heartbeat and, later, of <c>/estado</c>.</summary>
/// <param name="BatteryPercent">0 to 100, or null if the platform cannot tell.</param>
/// <param name="IsPluggedIn">Charger connected (mains or USB). Its loss is the power-cut alert of phase 2.</param>
/// <param name="BatteryTemperatureCelsius">Battery temperature, or null if unknown.</param>
/// <param name="IsBatteryOptimizationIgnored">
/// Whether the app is exempt from battery optimization (Doze). The exemption was seen to vanish on its own on the
/// OPPO (2026-10-23), so every heartbeat says it; null if the platform cannot tell.
/// </param>
public sealed record DeviceStatus(int? BatteryPercent, bool IsPluggedIn, double? BatteryTemperatureCelsius, bool? IsBatteryOptimizationIgnored = null);

/// <summary>The platform's view of battery and power; faked in tests.</summary>
public interface IDeviceStatusSource
{
    DeviceStatus Read();
}
