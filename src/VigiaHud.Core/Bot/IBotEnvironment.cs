using VigiaHud.Core.Device;

namespace VigiaHud.Core.Bot;

/// <summary>What the bot needs from the phone, given by the app (step 5) and faked in tests.</summary>
public interface IBotEnvironment
{
    /// <summary>The phone's zone, for every time the owner reads.</summary>
    TimeZoneInfo Zone { get; }

    /// <summary>How long the watch has been running.</summary>
    TimeSpan WatchingFor { get; }

    DeviceStatus ReadDevice();

    /// <summary>A JPEG of the room now, from the camera stream; null if the camera is not ready.</summary>
    Task<byte[]?> TakePhotoAsync(CancellationToken cancellationToken);
}

/// <summary>The little the bot must remember across restarts; kept by the app in the phone's preferences.</summary>
public interface IBotMemory
{
    /// <summary>When the last daily heartbeat was written (it may have gone out later, from the outbox).</summary>
    DateTimeOffset? LastDailyHeartbeat { get; set; }
}
