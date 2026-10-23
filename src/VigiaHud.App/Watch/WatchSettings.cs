namespace VigiaHud.App.Watch;

/// <summary>
/// What the owner last asked for, kept across reboots: the boot receiver only restarts a watch that was running.
/// Written by <see cref="IWatchController"/> on a successful start request and on every stop, never by the
/// service itself (a restart by Android is not a choice). Behind an interface so a fake can stand in for the phone.
/// </summary>
public interface IWatchSettings
{
    bool Enabled { get; set; }
}

/// <summary>The phone's copy, in MAUI preferences (credential-encrypted storage: readable once the user has unlocked).</summary>
public sealed class PreferencesWatchSettings : IWatchSettings
{
    private const string EnabledKey = "watch_enabled";

    public bool Enabled
    {
        get => Preferences.Default.Get(EnabledKey, false);
        set => Preferences.Default.Set(EnabledKey, value);
    }
}
