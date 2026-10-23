using Android.Content;
using VigiaHud.App.Watch;
using VigiaHud.Core.Logging;

namespace VigiaHud.App.Platforms.Android.Watch;

/// <summary>Starts and stops <see cref="WatchService"/> from the page. Must be called with the app visible (Android 12+ rule).</summary>
public sealed class AndroidWatchController : IWatchController
{
    private readonly IWatchLog _log;
    private readonly IWatchSettings _settings;

    public AndroidWatchController(IWatchLog log, IWatchSettings settings)
    {
        _log = log;
        _settings = settings;
        WatchService.RunningChanged += (_, _) => RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsRunning => WatchService.IsRunning;

    public event EventHandler? RunningChanged;

    public void Start()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var intent = new Intent(context, typeof(WatchService)).SetAction(WatchService.ActionStart);
            context.StartForegroundService(intent);
            // Remembered only once the request went through (DECISIONS 011). If the service then fails to start,
            // it says so in the log and the flag stays: a reboot is a fair second chance for a transient failure.
            _settings.Enabled = true;
        }
        catch (Exception error)
        {
            _log.Write(LogKind.Error, $"could not start the service: {error}");
        }
    }

    public void Stop()
    {
        _settings.Enabled = false;
        var context = global::Android.App.Application.Context;
        context.StartService(new Intent(context, typeof(WatchService)).SetAction(WatchService.ActionStop));
    }
}
