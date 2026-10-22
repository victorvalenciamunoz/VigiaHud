namespace VigiaHud.App.Watch;

/// <summary>
/// The platform side of the watch: a service that stays alive with the screen off. The page only asks it to
/// start or stop and shows whether it is running; what the watch does once running is decided by Core.
/// </summary>
public interface IWatchController
{
    bool IsRunning { get; }

    /// <summary>Raised on any thread when the service starts or stops; the page marshals to the UI thread.</summary>
    event EventHandler? RunningChanged;

    void Start();

    void Stop();
}
