namespace VigiaHud.App;

/// <summary>
/// The one screen of phase 1: whether the watch is running and the button to start or stop it. Layout only:
/// the service, the camera and the log come in the next steps, and every rule lives in Core.
/// </summary>
public partial class WatchPage : ContentPage
{
    public WatchPage()
    {
        InitializeComponent();
        Version.Text = $"v{AppInfo.Current.VersionString}";
    }
}
