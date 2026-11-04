namespace VigiaHud.Core.Recording;

/// <summary>
/// What a recording is: its name, when it started, the size of its grey frames, the size of the camera frames they
/// came from, how the camera's sensor is mounted (clockwise degrees from the phone upright, 90 on the OPPO) and how
/// often a frame was kept.
/// </summary>
public sealed record SequenceHeader(
    string Name,
    DateTimeOffset StartedAt,
    int Width,
    int Height,
    int SourceWidth,
    int SourceHeight,
    int SensorOrientation,
    TimeSpan FrameInterval);
