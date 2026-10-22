using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using VigiaHud.Core.Logging;

namespace VigiaHud.App.Platforms.Android.Watch;

/// <summary>
/// The foreground service that is the watch (phase 1, step 3): a permanent notification so Android keeps the process,
/// a partial wake lock so the CPU runs with the screen off, and a line in the log for every start, stop and failure.
/// Sticky: if Android kills it anyway, it asks to be restarted. The camera arrives in step 4; this step only has to
/// stay alive. Fixed Java name so adb and the manifest refer to it without the generated hash.
/// </summary>
[Service(Name = "com.vigiahud.app.WatchService", Exported = false, ForegroundServiceType = ForegroundService.TypeCamera)]
public sealed class WatchService : Service
{
    public const string ActionStart = "com.vigiahud.app.action.START";
    public const string ActionStop = "com.vigiahud.app.action.STOP";

    private const int NotificationId = 1;
    private const string ChannelId = "watch";

    private static readonly object Gate = new();
    private static bool _isRunning;

    private PowerManager.WakeLock? _wakeLock;
    private IWatchLog? _log;

    public static bool IsRunning
    {
        get { lock (Gate) { return _isRunning; } }
    }

    /// <summary>Raised on the service's thread when <see cref="IsRunning"/> changes.</summary>
    public static event EventHandler? RunningChanged;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        _log ??= IPlatformApplication.Current?.Services.GetService<IWatchLog>();

        if (intent?.Action == ActionStop)
        {
            _log?.Write(LogKind.Stop, "service stop requested");
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        try
        {
            if (!IsRunning)
            {
                StartForegroundWithNotification();
                AcquireWakeLock();
                // The log line goes first: the page refreshes its tail when it hears RunningChanged.
                var restarted = intent is null || flags.HasFlag(StartCommandFlags.Redelivery) || flags.HasFlag(StartCommandFlags.Retry);
                _log?.Write(LogKind.Start, restarted ? "service restarted by Android" : "service started");
                SetRunning(true);
            }
        }
        catch (Exception error)
        {
            _log?.Write(LogKind.Error, $"service failed to start: {error}");
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        return StartCommandResult.Sticky;
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        // The user swiped the app away from recents. The service goes on (stopWithTask is false), but it is worth knowing.
        _log?.Write(LogKind.Info, "task removed from recents; service continues");
        base.OnTaskRemoved(rootIntent);
    }

    public override void OnDestroy()
    {
        ReleaseWakeLock();
        if (IsRunning)
        {
            _log?.Write(LogKind.Stop, "service destroyed");
            SetRunning(false);
        }

        base.OnDestroy();
    }

    private void StartForegroundWithNotification()
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        var channel = new NotificationChannel(ChannelId, "Vigilancia", NotificationImportance.Low)
        {
            Description = "La vigilancia en marcha. Sin esta notificación, Android cerraría el servicio.",
        };
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);

        var openApp = PendingIntent.GetActivity(
            this, 0, PackageManager!.GetLaunchIntentForPackage(PackageName!), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("VigíaHud")
            .SetContentText("Vigilando.")
            .SetSmallIcon(Resource.Drawable.ic_stat_vigia)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetContentIntent(openApp)
            .SetCategory(Notification.CategoryService)
            .SetVisibility(NotificationVisibility.Public)
            .Build();

        // The camera type exists from Android 11 (API 30). On Android 10 the service is promoted without a type (DECISIONS 003).
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeCamera);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }
    }

    private void AcquireWakeLock()
    {
        var power = (PowerManager)GetSystemService(PowerService)!;
        _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "VigiaHud::WatchService");
        _wakeLock!.SetReferenceCounted(false);
        _wakeLock.Acquire();
    }

    private void ReleaseWakeLock()
    {
        if (_wakeLock is { IsHeld: true })
        {
            _wakeLock.Release();
        }

        _wakeLock = null;
    }

    private static void SetRunning(bool running)
    {
        lock (Gate)
        {
            _isRunning = running;
        }

        RunningChanged?.Invoke(null, EventArgs.Empty);
    }
}
