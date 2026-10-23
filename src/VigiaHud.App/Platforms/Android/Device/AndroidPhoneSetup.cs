using Android.Content;
using Android.OS;
using Android.Provider;
using VigiaHud.App.Device;
using VigiaHud.Core.Logging;
using Uri = Android.Net.Uri;

namespace VigiaHud.App.Platforms.Android.Device;

/// <summary>
/// Battery optimization and the app's settings page, through the official intents
/// (ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS needs the permission of the same name in the manifest).
/// ColorOS adds its own switches (auto start, background running) that only the user can reach, from the app's page.
/// </summary>
public sealed class AndroidPhoneSetup : IPhoneSetup
{
    private readonly IWatchLog _log;

    public AndroidPhoneSetup(IWatchLog log)
    {
        _log = log;
    }

    public bool IsBatteryOptimizationIgnored
    {
        get
        {
            var context = global::Android.App.Application.Context;
            var power = (PowerManager?)context.GetSystemService(Context.PowerService);
            return power?.IsIgnoringBatteryOptimizations(context.PackageName!) ?? false;
        }
    }

    public void RequestIgnoreBatteryOptimization() =>
        Launch(new Intent(Settings.ActionRequestIgnoreBatteryOptimizations, Uri.Parse("package:" + global::Android.App.Application.Context.PackageName)));

    public void OpenAppSettings() =>
        Launch(new Intent(Settings.ActionApplicationDetailsSettings, Uri.Parse("package:" + global::Android.App.Application.Context.PackageName)));

    private void Launch(Intent intent)
    {
        try
        {
            intent.AddFlags(ActivityFlags.NewTask);
            global::Android.App.Application.Context.StartActivity(intent);
        }
        catch (Exception error)
        {
            _log.Write(LogKind.Error, $"could not open {intent.Action}: {error.GetType().Name}: {error.Message}");
        }
    }
}
