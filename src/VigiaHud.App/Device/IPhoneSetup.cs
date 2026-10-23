namespace VigiaHud.App.Device;

/// <summary>
/// The phone settings the watch depends on and cannot change by itself: the user has to grant them in system
/// screens. The app can only check, ask, and open the right screen.
/// </summary>
public interface IPhoneSetup
{
    /// <summary>True when the app is exempt from battery optimization (Doze), so its wake lock is honored.</summary>
    bool IsBatteryOptimizationIgnored { get; }

    /// <summary>Shows the system dialog that asks the user to exempt the app from battery optimization.</summary>
    void RequestIgnoreBatteryOptimization();

    /// <summary>Opens the app's own page in the system settings (auto start, background running, permissions).</summary>
    void OpenAppSettings();
}
