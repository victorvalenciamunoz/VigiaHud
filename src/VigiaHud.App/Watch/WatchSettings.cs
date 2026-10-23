namespace VigiaHud.App.Watch;

/// <summary>
/// What the owner last asked for, kept across reboots: the boot receiver only restarts a watch that was running.
/// Written by the page on INICIAR and PARAR, never by the service itself (a restart by Android is not a choice).
/// </summary>
public static class WatchSettings
{
    private const string EnabledKey = "watch_enabled";

    public static bool Enabled
    {
        get => Preferences.Default.Get(EnabledKey, false);
        set => Preferences.Default.Set(EnabledKey, value);
    }
}
