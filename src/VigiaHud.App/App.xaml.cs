using VigiaHud.App.Hud;
using VigiaHud.Core.Logging;

namespace VigiaHud.App;

public partial class App : Application
{
    private readonly IWatchLog _log;

    public App(IWatchLog log)
    {
        _log = log;
        InitializeComponent();
        // Before any page is built, so their DynamicResource colors are there from the first frame.
        HudTheme.Apply(this);
        // A HUD of the family: always dark, whatever the system theme.
        UserAppTheme = AppTheme.Dark;

        // Whatever kills the app leaves a trace: the log is how we learn why the watch died.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("unhandled", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => LogCrash("unobserved task", e.Exception);
        _log.Write(LogKind.Start, $"app opened v{AppInfo.Current.VersionString}");
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());

    private void LogCrash(string origin, object? error)
    {
        // Type, message and the first frames: enough to find it, short enough for one line.
        var text = error?.ToString() ?? "unknown";
        if (text.Length > 1500)
        {
            text = text[..1500] + "…";
        }

        _log.Write(LogKind.Error, $"{origin}: {text}");
    }
}
