using Android.Content;
using Android.Hardware;
using Android.Runtime;
using VigiaHud.Core.Device;

namespace VigiaHud.App.Platforms.Android.Device;

/// <summary>
/// One accelerometer reading per photo, to know which way the phone stands (DECISIONS 022). The sensor is listened
/// to only for that moment, so the watch spends nothing on it. Lying flat, or without an answer within a second, the
/// last rotation it knew counts; before any, upright.
/// </summary>
public sealed class GravityReader
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(1);

    private readonly SensorManager? _sensors;
    private readonly Sensor? _accelerometer;
    private int? _lastRotation;

    public GravityReader(Context context)
    {
        _sensors = (SensorManager?)context.GetSystemService(Context.SensorService);
        _accelerometer = _sensors?.GetDefaultSensor(SensorType.Accelerometer);
    }

    /// <summary>The phone's rotation from upright, clockwise: 0, 90, 180 or 270.</summary>
    public async Task<int> RotationAsync(CancellationToken cancellationToken)
    {
        if (_sensors is null || _accelerometer is null)
        {
            return _lastRotation ?? 0;
        }

        var reading = new Listener();
        _sensors.RegisterListener(reading, _accelerometer, SensorDelay.Normal);
        try
        {
            var values = await reading.First.Task.WaitAsync(Wait, cancellationToken).ConfigureAwait(false);
            if (PhotoOrientation.DeviceRotation(values[0], values[1], values[2]) is { } rotation)
            {
                _lastRotation = rotation;
            }
        }
        catch (TimeoutException)
        {
            // No reading in time: keep what was known.
        }
        finally
        {
            _sensors.UnregisterListener(reading);
        }

        return _lastRotation ?? 0;
    }

    private sealed class Listener : Java.Lang.Object, ISensorEventListener
    {
        public TaskCompletionSource<float[]> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnSensorChanged(SensorEvent? e)
        {
            if (e?.Values is { Count: >= 3 } values)
            {
                First.TrySetResult([values[0], values[1], values[2]]);
            }
        }

        public void OnAccuracyChanged(Sensor? sensor, [GeneratedEnum] SensorStatus accuracy)
        {
        }
    }
}
