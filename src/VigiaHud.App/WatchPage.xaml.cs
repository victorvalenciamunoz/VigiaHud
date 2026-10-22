using VigiaHud.Core.Logging;

namespace VigiaHud.App;

/// <summary>
/// The one screen of phase 1: whether the watch is running, the button to start or stop it, and the tail of the
/// log so the phone can be checked without a computer. Layout only: every rule lives in Core.
/// </summary>
public partial class WatchPage : ContentPage
{
    private const int TailLength = 8;

    private readonly RotatingFileLogOptions _logOptions;

    public WatchPage(RotatingFileLogOptions logOptions)
    {
        _logOptions = logOptions;
        InitializeComponent();
        Version.Text = $"v{AppInfo.Current.VersionString}";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ShowLogTail();
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
