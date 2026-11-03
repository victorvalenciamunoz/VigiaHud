using VigiaHud.Core.Bot;
using VigiaHud.Core.Device;

namespace VigiaHud.App.Platforms.Android.Bot;

/// <summary>
/// What the bot sees of the phone: the phone's own zone, the battery from the same source as the heartbeat line, the
/// time since the watch started, and a photo for <c>/foto</c> taken by the watch service.
/// </summary>
public sealed class AndroidBotEnvironment(
    IDeviceStatusSource device,
    Func<TimeSpan> watchingFor,
    Func<CancellationToken, Task<byte[]?>> takePhoto) : IBotEnvironment
{
    public TimeZoneInfo Zone => TimeZoneInfo.Local;

    public TimeSpan WatchingFor => watchingFor();

    public DeviceStatus ReadDevice() => device.Read();

    public Task<byte[]?> TakePhotoAsync(CancellationToken cancellationToken) => takePhoto(cancellationToken);
}
