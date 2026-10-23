using Android.Content;
using VigiaHud.App.Watch;
using VigiaHud.Core.Logging;

namespace VigiaHud.App.Platforms.Android.Watch;

/// <summary>Starts and stops <see cref="WatchService"/> from the page. Must be called with the app visible (Android 12+ rule).</summary>
public sealed class AndroidWatchController : IWatchController
{
    private readonly IWatchLog _log;

    public AndroidWatchController(IWatchLog log)
    {
        _log = log;
        WatchService.RunningChanged += (_, _) => RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsRunning => WatchService.IsRunning;

    public event EventHandler? RunningChanged;

    public void Start()
    {
        // The owner's choice, remembered for the boot receiver (DECISIONS 011).
        WatchSettings.Enabled = true;
        try
        {
            var context = global::Android.App.Application.Context;
            var intent = new Intent(context, typeof(WatchService)).SetAction(WatchService.ActionStart);
            context.StartForegroundService(intent);
        }
        catch (Exception error)
        {
            _log.Write(LogKind.Error, $"could not start the service: {error}");
        }
    }

    public void Stop()
    {
        WatchSettings.Enabled = false;
        var context = global::Android.App.Application.Context;
        context.StartService(new Intent(context, typeof(WatchService)).SetAction(WatchService.ActionStop));
    }
}
