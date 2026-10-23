using Android.App;
using Android.Content;
using VigiaHud.App.Watch;
using VigiaHud.Core.Logging;

namespace VigiaHud.App.Platforms.Android.Watch;

/// <summary>
/// The alarm that outlives the process (DECISIONS 012): every few minutes it checks that the watch service is
/// running and, if the owner had it on and it is not, starts it again. Rescheduled from here as well as from the
/// service, so a dead process cannot take the watchdog with it. START_STICKY alone was seen to fail on the OPPO:
/// Android stopped the restarted service as idle before it could run.
/// </summary>
[BroadcastReceiver(Name = "com.vigiahud.app.WatchdogReceiver", Exported = false, Enabled = true)]
public sealed class WatchdogReceiver : BroadcastReceiver
{
    public const string ActionCheck = "com.vigiahud.app.action.WATCHDOG";
    private const int RequestCode = 7;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != ActionCheck)
        {
            return;
        }

        var services = IPlatformApplication.Current?.Services;
        var log = services?.GetService<IWatchLog>();
        var settings = services?.GetService<IWatchSettings>() ?? new PreferencesWatchSettings();

        if (!settings.Enabled)
        {
            // Stopped on purpose: nothing to revive, and no more alarms.
            Cancel(context);
            return;
        }

        Schedule(context);
        if (WatchService.IsRunning)
        {
            return;
        }

        try
        {
            context.StartForegroundService(new Intent(context, typeof(WatchService)).SetAction(WatchService.ActionStart));
            log?.Write(LogKind.Info, "watchdog: service was not running; start requested");
        }
        catch (Exception error)
        {
            log?.Write(LogKind.Error, $"watchdog: could not start the service: {error.GetType().Name}: {error.Message}");
        }
    }

    /// <summary>Arms the next check. Inexact and allowed while idle: Doze may hold it a few minutes, never cancel it.</summary>
    public static void Schedule(Context context)
    {
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        var at = Java.Lang.JavaSystem.CurrentTimeMillis() + (long)Interval.TotalMilliseconds;
        alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at, Pending(context));
    }

    public static void Cancel(Context context)
    {
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        alarms.Cancel(Pending(context));
    }

    private static PendingIntent Pending(Context context)
    {
        var intent = new Intent(context, typeof(WatchdogReceiver)).SetAction(ActionCheck);
        return PendingIntent.GetBroadcast(context, RequestCode, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }
}
