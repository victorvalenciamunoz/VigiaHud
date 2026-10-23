using VigiaHud.App.Device;
using VigiaHud.App.Watch;
using VigiaHud.Core.Logging;

namespace VigiaHud.App;

/// <summary>
/// The one screen of phase 1: whether the watch is running, the button to start or stop it, and the tail of the
/// log so the phone can be checked without a computer. Layout only: the service is the platform's, and every
/// rule lives in Core.
/// </summary>
public partial class WatchPage : ContentPage
{
    private const int TailLength = 8;

    private readonly RotatingFileLogOptions _logOptions;
    private readonly IWatchController _watch;
    private readonly IPhoneSetup _phone;

    public WatchPage(RotatingFileLogOptions logOptions, IWatchController watch, IPhoneSetup phone)
    {
        _logOptions = logOptions;
        _watch = watch;
        _phone = phone;
        InitializeComponent();
        Version.Text = $"v{AppInfo.Current.VersionString}";
        _watch.RunningChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            ShowWatchState();
            ShowLogTail();
        });
    }

    private bool _watchingResume;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Back from a system dialog or the settings app, the page is not "appearing" again but the window resumes:
        // that is when the battery exemption may have changed.
        if (!_watchingResume && Window is { } window)
        {
            _watchingResume = true;
            window.Resumed += (_, _) => Refresh();
        }

        Refresh();
    }

    private void Refresh()
    {
        ShowWatchState();
        ShowPhoneSetup();
        ShowLogTail();
    }

    private void OnBatteryClicked(object? sender, EventArgs e) => _phone.RequestIgnoreBatteryOptimization();

    private void OnAppSettingsClicked(object? sender, EventArgs e) => _phone.OpenAppSettings();

    private void ShowPhoneSetup()
    {
        var ignored = _phone.IsBatteryOptimizationIgnored;
        BatteryStatus.Text = ignored
            ? "Optimización de batería: excluida. Android no dormirá la vigilancia."
            : "Optimización de batería: activa. Android puede dormir la vigilancia; pulsa EXCLUIR DE BATERÍA.";
        BatteryStatus.TextColor = (Color)Application.Current!.Resources[ignored ? "HudGood" : "HudAlert"];
        BatteryButton.IsVisible = !ignored;
    }

    private async void OnToggleClicked(object? sender, EventArgs e)
    {
        if (_watch.IsRunning)
        {
            _watch.Stop();
            return;
        }

        // The camera is the watch; without it there is nothing to start. The service must not ask: it has no UI.
        var camera = await Permissions.RequestAsync<Permissions.Camera>();
        if (camera != PermissionStatus.Granted)
        {
            Detail.Text = "Sin permiso de cámara no hay vigilancia. Concédelo en Ajustes > Aplicaciones > VigíaHud.";
            return;
        }

        // Android 13+ needs leave to show the permanent notification; on this phone (Android 10) it is granted by default.
        var notifications = await Permissions.RequestAsync<Permissions.PostNotifications>();
        if (notifications != PermissionStatus.Granted)
        {
            Detail.Text = "Sin permiso de notificaciones Android cerraría el servicio. Concédelo en Ajustes.";
            return;
        }

        _watch.Start();
    }

    private void ShowWatchState()
    {
        var running = _watch.IsRunning;
        Status.Text = running ? "Vigilando." : "Parada.";
        Status.TextColor = (Color)Application.Current!.Resources[running ? "HudGood" : "HudTextMuted"];
        Detail.Text = running
            ? "El servicio sigue con la pantalla apagada. Puedes salir de la app."
            : "Pulsa INICIAR para dejar el servicio vigilando con la pantalla apagada.";
        Toggle.Text = running ? "PARAR" : "INICIAR";
    }

    private void OnRefreshLogClicked(object? sender, EventArgs e) => ShowLogTail();

    private void ShowLogTail()
    {
        LogLines.Clear();
        var entries = LogReader.ReadLast(_logOptions, TailLength);
        if (entries.Count == 0)
        {
            LogLines.Add(MutedLabel("Sin entradas todavía."));
            return;
        }

        foreach (var entry in entries)
        {
            // Local time for the eye; the file keeps UTC.
            var local = entry.At.ToLocalTime();
            var line = $"{local:dd/MM HH:mm:ss}  {entry.Kind.ToString().ToLowerInvariant()}  {entry.Message}";
            LogLines.Add(entry.Kind == LogKind.Error ? AlertLabel(line) : MutedLabel(line));
        }
    }

    private static Label MutedLabel(string text) => new()
    {
        Text = text,
        FontFamily = "OpenSansRegular",
        FontSize = 11,
        TextColor = (Color)Application.Current!.Resources["HudTextMuted"],
        LineBreakMode = LineBreakMode.TailTruncation,
    };

    private static Label AlertLabel(string text) => new()
    {
        Text = text,
        FontFamily = "OpenSansRegular",
        FontSize = 11,
        TextColor = (Color)Application.Current!.Resources["HudAlert"],
        LineBreakMode = LineBreakMode.TailTruncation,
    };
}
