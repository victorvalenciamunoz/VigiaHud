using Android.App;
using Android.Content;
using VigiaHud.App.Watch;
using VigiaHud.Core.Logging;

namespace VigiaHud.App.Platforms.Android.Watch;

/// <summary>
/// Brings the watch back after a reboot or an update of the app, but only if the owner had it running
/// (<see cref="WatchSettings.Enabled"/>): a watch stopped on purpose stays stopped (DECISIONS 011).
/// On Android 15+ a camera foreground service may not start from BOOT_COMPLETED; that is logged, not hidden.
/// </summary>
[BroadcastReceiver(Name = "com.vigiahud.app.BootReceiver", Exported = true, Enabled = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced])]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action is not (Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced))
        {
            return;
        }

        var log = IPlatformApplication.Current?.Services.GetService<IWatchLog>();
        var reason = intent.Action == Intent.ActionBootCompleted ? "boot" : "update";
        if (!WatchSettings.Enabled)
        {
            log?.Write(LogKind.Info, $"{reason}: watch was off, not started");
            return;
        }

        try
        {
            var start = new Intent(context, typeof(WatchService)).SetAction(WatchService.ActionStart);
            context.StartForegroundService(start);
            log?.Write(LogKind.Start, $"service start requested after {reason}");
        }
        catch (Exception error)
        {
            log?.Write(LogKind.Error, $"could not start the service after {reason}: {error.GetType().Name}: {error.Message}");
        }
    }
}
