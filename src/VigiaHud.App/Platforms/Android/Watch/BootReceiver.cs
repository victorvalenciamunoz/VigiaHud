using Android.App;
using Android.Content;
using VigiaHud.App.Watch;
using VigiaHud.Core.Logging;

namespace VigiaHud.App.Platforms.Android.Watch;

/// <summary>
/// Brings the watch back after a reboot or an update of the app, but only if the owner had it running
/// (<see cref="IWatchSettings.Enabled"/>): a watch stopped on purpose stays stopped (DECISIONS 011).
/// BOOT_COMPLETED is delivered only once the user has unlocked the phone, so an unattended restart needs a phone
/// without PIN or pattern (docs/MOVIL.md). On Android 15+ a camera foreground service may not start from here;
/// that is logged, not hidden, and if the watch log itself is unreachable the outcome still goes to logcat.
/// </summary>
[BroadcastReceiver(Name = "com.vigiahud.app.BootReceiver", Exported = true, Enabled = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced])]
public sealed class BootReceiver : BroadcastReceiver
{
    private const string Tag = "VigiaHud";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action is not (Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced))
        {
            return;
        }

        var services = IPlatformApplication.Current?.Services;
        var log = services?.GetService<IWatchLog>();
        var settings = services?.GetService<IWatchSettings>() ?? new PreferencesWatchSettings();
        var reason = intent.Action == Intent.ActionBootCompleted ? "boot" : "update";

        if (!settings.Enabled)
        {
            Say(log, LogKind.Info, $"{reason}: watch was off, not started");
            return;
        }

        try
        {
            var start = new Intent(context, typeof(WatchService)).SetAction(WatchService.ActionStart);
            context.StartForegroundService(start);
            // Not a LogKind.Start: the service writes its own "service started" line, and the judge counts those.
            Say(log, LogKind.Info, $"{reason}: service start requested");
        }
        catch (Exception error)
        {
            Say(log, LogKind.Error, $"{reason}: could not start the service: {error.GetType().Name}: {error.Message}");
        }
    }

    private static void Say(IWatchLog? log, LogKind kind, string message)
    {
        if (log is null)
        {
            global::Android.Util.Log.Error(Tag, $"no watch log in the receiver; {kind}: {message}");
            return;
        }

        log.Write(kind, message);
    }
}
