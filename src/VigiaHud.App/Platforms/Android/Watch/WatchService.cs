using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using VigiaHud.App.Bot;
using VigiaHud.App.Platforms.Android.Bot;
using VigiaHud.Core.Bot;
using VigiaHud.Core.Device;
using VigiaHud.Core.Logging;
using VigiaHud.Core.Telegram;
using VigiaHud.Core.Watch;

namespace VigiaHud.App.Platforms.Android.Watch;

/// <summary>
/// The foreground service that is the watch (phase 1): a permanent notification so Android keeps the process,
/// a partial wake lock so the CPU runs with the screen off (timed and renewed every minute: ColorOS was seen to drop
/// one held for days, DECISIONS 013), a frame from the camera every few seconds, and a line
/// in the log for every start, stop, failure, minute of captures and heartbeat (step 4). Core decides when each
/// line is due (<see cref="WatchReporter"/>); this class only drives the camera and the clock. Sticky: if Android
/// kills it anyway, it asks to be restarted. Fixed Java name so adb and the manifest refer to it without the hash.
/// </summary>
[Service(Name = "com.vigiahud.app.WatchService", Exported = false, ForegroundServiceType = ForegroundService.TypeCamera)]
public sealed class WatchService : Service
{
    public const string ActionStart = "com.vigiahud.app.action.START";
    public const string ActionStop = "com.vigiahud.app.action.STOP";

    private const int NotificationId = 1;
    private const string ChannelId = "watch";
    private const int MaxSnapshots = 200;

    /// <summary>Long enough to outlast a missed minute or two, short enough that a forgotten lock dies on its own.</summary>
    private static readonly TimeSpan WakeLockHold = TimeSpan.FromMinutes(15);

    /// <summary>A frame older than this is not "now": the camera is lost or stalled, and <c>/foto</c> says so.</summary>
    private static readonly TimeSpan FreshFrame = TimeSpan.FromSeconds(10);

    /// <summary>How long a large photo may take before <c>/foto</c> falls back to a stream frame.</summary>
    private static readonly TimeSpan StillTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Which way the phone stands, read once per photo so the photo comes out upright (DECISIONS 022).</summary>
    private Platforms.Android.Device.GravityReader? _gravity;

    private static readonly object Gate = new();
    private static bool _isRunning;
    private static WatchService? _current;

    private readonly Handler _main = new(Looper.MainLooper!);

    private PowerManager.WakeLock? _wakeLock;
    private IWatchLog? _log;
    private TimeProvider? _clock;
    private IDeviceStatusSource? _device;
    private WatchTimings? _timings;
    private WatchReporter? _reporter;
    private SnapshotStore? _snapshots;
    private CameraCapturer? _camera;
    private Java.Lang.Runnable? _tick;
    private DateTimeOffset _startedAt;
    private readonly object _frameGate = new();
    private (byte[] Jpeg, DateTimeOffset At)? _latestFrame;
    private CancellationTokenSource? _botStop;
    private NetworkWatcher? _network;

    public static bool IsRunning
    {
        get { lock (Gate) { return _isRunning; } }
    }

    /// <summary>Raised on the service's thread when <see cref="IsRunning"/> changes.</summary>
    public static event EventHandler? RunningChanged;

    public override IBinder? OnBind(Intent? intent) => null;

    /// <summary>
    /// Called by the watchdog alarm, which wakes the CPU even from deep sleep: renew the wake lock and, if the camera
    /// is lost, reopen it now. Handler timers count awake time only, so on a sleeping phone they may never fire.
    /// </summary>
    public static void CheckFromWatchdog()
    {
        var service = _current;
        if (service is null)
        {
            return;
        }

        service._main.Post(() =>
        {
            if (!IsRunning)
            {
                return;
            }

            service.RenewWakeLock();
            service._camera?.EnsureStreaming();
        });
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var services = IPlatformApplication.Current?.Services;
        _log ??= services?.GetService<IWatchLog>();
        _clock ??= services?.GetService<TimeProvider>() ?? TimeProvider.System;
        _device ??= services?.GetService<IDeviceStatusSource>();
        _timings ??= services?.GetService<WatchTimings>() ?? WatchTimings.Default;

        if (intent?.Action == ActionStop)
        {
            _log?.Write(LogKind.Stop, "service stop requested");
            // Stopped on purpose: the watchdog must not bring it back.
            WatchdogReceiver.Cancel(this);
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        try
        {
            if (!IsRunning)
            {
                // Started from the page, from boot or after an update: the permission may have gone since the owner
                // granted it. Without it the camera would fail and retry every 30 s forever without naming the cause.
                if (CheckSelfPermission(global::Android.Manifest.Permission.Camera) != Permission.Granted)
                {
                    _log?.Write(LogKind.Error, "camera permission missing; the watch cannot start. Open the app and press INICIAR.");
                    StopSelf();
                    return StartCommandResult.NotSticky;
                }

                StartForegroundWithNotification();
                AcquireWakeLock();
                // The log line goes first: the page refreshes its tail when it hears RunningChanged.
                var restarted = intent is null || flags.HasFlag(StartCommandFlags.Redelivery) || flags.HasFlag(StartCommandFlags.Retry);
                _log?.Write(LogKind.Start, restarted ? "service restarted by Android" : "service started");
                // Whether Android may put us to sleep: the first thing to look at when the judge finds a gap.
                var phone = services?.GetService<VigiaHud.App.Device.IPhoneSetup>();
                if (phone is not null)
                {
                    _log?.Write(LogKind.Info, $"battery optimization ignored={(phone.IsBatteryOptimizationIgnored ? "yes" : "no")}");
                }

                StartCapturing();
                StartBot();
                // The alarm that outlives this process (DECISIONS 012): if we die and the sticky restart fails, it starts us again.
                WatchdogReceiver.Schedule(this);
                _current = this;
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
        _current = null;
        StopBot();
        StopCapturing();
        ReleaseWakeLock();
        if (IsRunning)
        {
            _log?.Write(LogKind.Stop, "service destroyed");
            SetRunning(false);
        }

        base.OnDestroy();
    }

    private void StartCapturing()
    {
        if (_log is null)
        {
            throw new InvalidOperationException("The watch log is not registered.");
        }

        var now = _clock!.GetUtcNow();
        _startedAt = now;
        _reporter = new WatchReporter(_timings!, now);
        _snapshots = new SnapshotStore(Path.Combine(FilesDir!.AbsolutePath, "snapshots"), MaxSnapshots);
        _camera = new CameraCapturer(this, _log, OnFrame, OnFrameFailed);
        _camera.Open();

        // The clock of the watch: one capture request per interval, on the main thread, while the wake lock keeps it ticking.
        var intervalMs = (long)_timings!.CaptureInterval.TotalMilliseconds;
        _tick = new Java.Lang.Runnable(() =>
        {
            _camera?.CaptureOne();
            if (_tick is not null)
            {
                _main.PostDelayed(_tick, intervalMs);
            }
        });
        _main.PostDelayed(_tick, intervalMs);
    }

    private void StopCapturing()
    {
        if (_tick is not null)
        {
            _main.RemoveCallbacks(_tick);
            _tick = null;
        }

        _camera?.Dispose();
        _camera = null;
        _reporter = null;
    }

    private void OnFrame(byte[] jpeg) => _main.Post(() =>
    {
        if (_reporter is null)
        {
            return;
        }

        var now = _clock!.GetUtcNow();
        lock (_frameGate)
        {
            _latestFrame = (jpeg, now);
        }

        HandleTick(_reporter.OnFrame(now), now, jpeg);
    });

    /// <summary>
    /// For <c>/foto</c>, from the bot's thread: a large photo from the camera, or the newest stream frame if the camera
    /// does not give one in time, or null if there is no fresh frame either.
    /// </summary>
    private async Task<byte[]?> TakePhotoAsync(CancellationToken cancellationToken)
    {
        if (_camera is { } camera)
        {
            try
            {
                _gravity ??= new Platforms.Android.Device.GravityReader(this);
                var rotation = await _gravity.RotationAsync(cancellationToken).ConfigureAwait(false);
                var turn = VigiaHud.Core.Device.PhotoOrientation.JpegOrientation(camera.SensorOrientation, rotation);
                var asked = System.Diagnostics.Stopwatch.StartNew();
                if (await camera.TakeStillAsync(turn).WaitAsync(StillTimeout, cancellationToken).ConfigureAwait(false) is { } still)
                {
                    // What is left until "telegram: answered /foto" is the upload.
                    _log?.Write(LogKind.Info, $"photo: phone rotated {rotation}, jpeg turned {turn}, {still.Length / 1024} KB in {asked.ElapsedMilliseconds} ms");
                    return still;
                }
            }
            catch (TimeoutException)
            {
                _log?.Write(LogKind.Info, $"photo: no answer from the camera in {StillTimeout.TotalSeconds:0} s; sending a stream frame");
            }
        }

        return LatestFrame();
    }

    /// <summary>The newest frame if it is fresh, for <c>/foto</c>; called from the bot's thread.</summary>
    private byte[]? LatestFrame()
    {
        lock (_frameGate)
        {
            return _latestFrame is { } frame && _clock!.GetUtcNow() - frame.At <= FreshFrame ? frame.Jpeg : null;
        }
    }

    /// <summary>
    /// Starts the Telegram bot (phase 2) if the phone has its secrets (DECISIONS 021). Without them the watch runs as in
    /// phase 1 and says so in the log. The bot runs on its own thread and never stops the watch: a fault there is logged.
    /// </summary>
    private void StartBot()
    {
        var path = Path.Combine(FilesDir!.AbsolutePath, TelegramSecrets.FileName);
        var secrets = File.Exists(path) ? TelegramSecrets.TryParse(File.ReadAllText(path)) : null;
        if (secrets is null || _device is null)
        {
            _log!.Write(LogKind.Info, File.Exists(path) ? "telegram: secrets file unreadable; bot off" : "telegram: not configured; bot off");
            return;
        }

        var environment = new AndroidBotEnvironment(_device, () => _clock!.GetUtcNow() - _startedAt, TakePhotoAsync);
        var client = new TelegramClient(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, secrets.Token);
        var runner = new BotRunner(client, secrets.ChatId, environment, new PreferencesBotMemory(), _clock!, _log!);
        var log = _log!;
        _network = new NetworkWatcher(this, () =>
        {
            if (runner.NetworkAvailable())
            {
                log.Write(LogKind.Info, "telegram: network back; retrying now");
            }
        });
        _botStop = new CancellationTokenSource();
        var stop = _botStop.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await runner.RunAsync(stop).ConfigureAwait(false);
            }
            catch (System.OperationCanceledException) when (stop.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                log.Write(LogKind.Error, $"telegram: bot stopped: {error.GetType().Name}: {error.Message}");
            }
        });
    }

    private void StopBot()
    {
        _network?.Dispose();
        _network = null;
        _botStop?.Cancel();
        _botStop?.Dispose();
        _botStop = null;
    }

    private void OnFrameFailed(string reason) => _main.Post(() =>
    {
        if (_reporter is null)
        {
            return;
        }

        var now = _clock!.GetUtcNow();
        HandleTick(_reporter.OnFrameFailed(now), now, null);
    });

    private void HandleTick(WatchTick tick, DateTimeOffset now, byte[]? jpeg)
    {
        if (tick.Report is { } report)
        {
            _log?.Write(LogKind.Capture, WatchMessages.Capture(report));
            // Once a minute: re-register the wake lock with the system, in case something dropped it.
            RenewWakeLock();
        }

        if (tick.Heartbeat)
        {
            var status = _device?.Read() ?? new DeviceStatus(null, false, null);
            _log?.Write(LogKind.Heartbeat, WatchMessages.Heartbeat(status, _reporter!.Uptime(now)));
        }

        if (tick.Snapshot && jpeg is not null && _snapshots is not null)
        {
            var (path, error) = _snapshots.Save(now, jpeg);
            _log?.Write(
                error is null ? LogKind.Info : LogKind.Error,
                error is null ? $"snapshot {Path.GetFileName(path)} ({jpeg.Length / 1024} KB)" : $"snapshot failed: {error}");
        }
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
        _wakeLock.Acquire((long)WakeLockHold.TotalMilliseconds);
    }

    /// <summary>
    /// A non-counted acquire goes to the system every time, so this re-creates the lock there if it was dropped
    /// (the object here still says "held" when that happens) and pushes the timeout forward.
    /// </summary>
    private void RenewWakeLock()
    {
        try
        {
            _wakeLock?.Acquire((long)WakeLockHold.TotalMilliseconds);
        }
        catch (Exception error)
        {
            _log?.Write(LogKind.Error, $"wake lock renew failed: {error.GetType().Name}: {error.Message}");
        }
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
