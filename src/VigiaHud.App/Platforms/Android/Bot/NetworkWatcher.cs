using Android.Content;
using Android.Net;

namespace VigiaHud.App.Platforms.Android.Bot;

/// <summary>
/// Hears from Android when a network becomes usable, so the bot retries at once instead of finishing a wait that
/// may have grown to 5 minutes (DECISIONS 023). Android also calls it once on registering if a network is there,
/// which is harmless: the bot ignores it unless it is waiting.
/// </summary>
public sealed class NetworkWatcher : IDisposable
{
    private readonly ConnectivityManager? _connectivity;
    private readonly Callback _callback;
    private bool _registered;

    public NetworkWatcher(Context context, Action available)
    {
        _connectivity = (ConnectivityManager?)context.GetSystemService(Context.ConnectivityService);
        _callback = new Callback(available);
        if (_connectivity is not null)
        {
            _connectivity.RegisterDefaultNetworkCallback(_callback);
            _registered = true;
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            try { _connectivity!.UnregisterNetworkCallback(_callback); } catch (Exception) { /* already gone */ }
            _registered = false;
        }

        _callback.Dispose();
    }

    private sealed class Callback(Action available) : ConnectivityManager.NetworkCallback
    {
        public override void OnAvailable(Network network) => available();
    }
}
