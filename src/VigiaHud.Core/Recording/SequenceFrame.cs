using VigiaHud.Core.Motion;

namespace VigiaHud.Core.Recording;

/// <summary>
/// One recorded frame: when it arrived, the grey picture exactly as the detector sees it, and the same moment as a
/// colour JPEG, for a person to look at and label, and for phase 4, whose judge reuses these recordings (PLAN.md).
/// </summary>
public sealed class SequenceFrame(DateTimeOffset at, LumaImage luma, byte[] jpeg)
{
    public DateTimeOffset At { get; } = at;

    public LumaImage Luma { get; } = luma ?? throw new ArgumentNullException(nameof(luma));

    public byte[] Jpeg { get; } = jpeg ?? throw new ArgumentNullException(nameof(jpeg));
}
