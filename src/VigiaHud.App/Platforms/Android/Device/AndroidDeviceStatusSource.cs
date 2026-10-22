using Android.Content;
using Android.OS;
using VigiaHud.Core.Device;

namespace VigiaHud.App.Platforms.Android.Device;

/// <summary>
/// Battery and charger from the sticky ACTION_BATTERY_CHANGED broadcast, which also carries the temperature
/// that MAUI's Battery API does not expose.
/// </summary>
public sealed class AndroidDeviceStatusSource : IDeviceStatusSource
{
    public DeviceStatus Read()
    {
        var context = global::Android.App.Application.Context;
        var filter = new IntentFilter(Intent.ActionBatteryChanged);
        var intent = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? context.RegisterReceiver(null, filter, ReceiverFlags.NotExported)
            : context.RegisterReceiver(null, filter);
        if (intent is null)
        {
            return new DeviceStatus(null, false, null);
        }

        var level = intent.GetIntExtra(BatteryManager.ExtraLevel, -1);
        var scale = intent.GetIntExtra(BatteryManager.ExtraScale, -1);
        var plugged = intent.GetIntExtra(BatteryManager.ExtraPlugged, 0);
        var tenthsOfDegree = intent.GetIntExtra(BatteryManager.ExtraTemperature, int.MinValue);

        int? percent = level >= 0 && scale > 0 ? (int)Math.Round(100.0 * level / scale) : null;
        double? temperature = tenthsOfDegree == int.MinValue ? null : tenthsOfDegree / 10.0;
        return new DeviceStatus(percent, plugged != 0, temperature);
    }
}
