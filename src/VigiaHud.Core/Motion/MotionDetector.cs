namespace VigiaHud.Core.Motion;

/// <summary>What the detector made of one frame.</summary>
public enum MotionVerdict
{
    /// <summary>The first frame, or the first after a reset: nothing to compare with yet.</summary>
    First,

    /// <summary>Less than <see cref="MotionSettings.MinArea"/> changed.</summary>
    Still,

    /// <summary>Enough changed, but not yet for <see cref="MotionSettings.Consecutive"/> frames in a row.</summary>
    Changed,

    /// <summary>Enough changed for enough frames in a row: somebody, or something, moves.</summary>
    Motion,

    /// <summary>Nearly the whole picture changed: the light or the exposure, ignored.</summary>
    LightChange,
}

/// <param name="ChangedArea">Share of the watched picture that changed (0 to 1).</param>
/// <param name="BrightnessShift">The overall brightness change taken off before comparing, in levels.</param>
public sealed record MotionResult(MotionVerdict Verdict, double ChangedArea, int BrightnessShift)
{
    public bool IsMotion => Verdict == MotionVerdict.Motion;
}

/// <summary>
/// Motion from grey frames (PLAN, phase 3; DECISIONS 026): each frame is shrunk again, compared with the previous one
/// or with a slow background, and the overall brightness change is taken off first (the median difference, so a
/// person covering part of the picture does not drag it), because a light switched on or the camera adjusting its
/// exposure moves every pixel at once. A pixel has changed when what is left passes <see cref="MotionSettings.Threshold"/>;
/// there is motion when the changed share of the watched picture passes <see cref="MotionSettings.MinArea"/> for
/// <see cref="MotionSettings.Consecutive"/> frames in a row, unless it passes <see cref="MotionSettings.LightChangeArea"/>,
/// which is the light. The same class runs on the phone and in the console's judge. Not thread-safe: one per stream.
/// </summary>
public sealed class MotionDetector
{
    private readonly MotionSettings _settings;
    private readonly MotionMask _mask;
    private readonly int[] _histogram = new int[511];
    private bool[]? _ignored;
    private int _watched;
    private int _width;
    private int _height;
    private byte[]? _previous;
    private float[]? _background;
    private int _inRow;

    public MotionDetector(MotionSettings settings, MotionMask? mask = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        _settings = settings;
        _mask = mask ?? MotionMask.None;
    }

    /// <summary>Forgets the reference: the next frame is <see cref="MotionVerdict.First"/> (after a gap in the camera, say).</summary>
    public void Reset()
    {
        _previous = null;
        _background = null;
        _inRow = 0;
    }

    public MotionResult Observe(LumaImage frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var small = _settings.ReduceFactor == 1
            ? frame.Pixels
            : LumaImage.Reduce(frame.Pixels, frame.Width, frame.Height, frame.Width, _settings.ReduceFactor).Pixels;
        var (width, height) = (frame.Width / _settings.ReduceFactor, frame.Height / _settings.ReduceFactor);
        if (_ignored is null || width != _width || height != _height)
        {
            // A new size (first frame, or another camera): the mask in pixels and the reference start again.
            (_width, _height) = (width, height);
            _ignored = _mask.PixelsIgnored(width, height);
            _watched = _ignored.Count(i => !i);
            Reset();
        }

        if (_watched == 0 || (_previous is null && _background is null))
        {
            Remember(small, relearn: true);
            return new MotionResult(MotionVerdict.First, 0, 0);
        }

        var shift = MedianShift(small);
        var changed = 0;
        for (var i = 0; i < small.Length; i++)
        {
            if (!_ignored[i] && Math.Abs(small[i] - Reference(i) - shift) > _settings.Threshold)
            {
                changed++;
            }
        }

        var area = changed / (double)_watched;
        MotionVerdict verdict;
        if (area >= _settings.LightChangeArea)
        {
            verdict = MotionVerdict.LightChange;
            _inRow = 0;
        }
        else if (area >= _settings.MinArea)
        {
            _inRow++;
            verdict = _inRow >= _settings.Consecutive ? MotionVerdict.Motion : MotionVerdict.Changed;
        }
        else
        {
            verdict = MotionVerdict.Still;
            _inRow = 0;
        }

        // After the light changes the old background is of another room: it is learnt again from this frame.
        Remember(small, relearn: verdict == MotionVerdict.LightChange);
        return new MotionResult(verdict, area, shift);
    }

    private float Reference(int i) => _settings.Reference == MotionReference.Previous ? _previous![i] : _background![i];

    /// <summary>The median of (frame - reference) over the watched pixels, from a histogram of the differences.</summary>
    private int MedianShift(byte[] small)
    {
        Array.Clear(_histogram);
        for (var i = 0; i < small.Length; i++)
        {
            if (!_ignored![i])
            {
                _histogram[(int)MathF.Round(small[i] - Reference(i)) + 255]++;
            }
        }

        var half = (_watched + 1) / 2;
        var seen = 0;
        for (var d = 0; d < _histogram.Length; d++)
        {
            seen += _histogram[d];
            if (seen >= half)
            {
                return d - 255;
            }
        }

        return 0;
    }

    private void Remember(byte[] small, bool relearn)
    {
        if (_settings.Reference == MotionReference.Previous)
        {
            _previous = small;
            return;
        }

        if (relearn || _background is null)
        {
            _background = [.. small.Select(p => (float)p)];
            return;
        }

        var rate = (float)_settings.BackgroundRate;
        for (var i = 0; i < small.Length; i++)
        {
            _background[i] += rate * (small[i] - _background[i]);
        }
    }
}
