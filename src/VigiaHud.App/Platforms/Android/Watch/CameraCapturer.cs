using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Views;
using Java.Lang;
using VigiaHud.Core.Logging;
using Exception = System.Exception;
using Image = Android.Media.Image;
using Size = Android.Util.Size;
using Rect = Android.Graphics.Rect;

namespace VigiaHud.App.Platforms.Android.Watch;

/// <summary>
/// The back camera streaming at low resolution and the lowest frame rate it offers, with Camera2 on its own thread,
/// and one frame handed out as a JPEG each time <see cref="CaptureOne"/> asks. A stream rather than single stills
/// because a still from an idle session comes out black (exposure never converges; seen on the OPPO on 2026-10-22),
/// and because phase 3 will want frames continuously anyway. If Android takes the camera away, it is reopened after
/// a delay: the watch must survive the camera app being used, not die with it. Every failure goes to the log and to
/// <c>onFailure</c>; nothing here throws into the service.
/// </summary>
public sealed class CameraCapturer : IDisposable
{
    private const int MinWidth = 320;
    private const int JpegQuality = 70;
    private static readonly TimeSpan ReopenDelay = TimeSpan.FromSeconds(30);

    private readonly Context _context;
    private readonly IWatchLog _log;
    private readonly Action<byte[]> _onFrame;
    private readonly Action<string> _onFailure;
    private readonly HandlerThread _thread;
    private readonly Handler _handler;

    private CameraDevice? _camera;
    private CameraCaptureSession? _session;
    private ImageReader? _reader;
    private Size _size = new(MinWidth, MinWidth * 3 / 4);
    private bool _grabNext;
    private bool _disposed;

    public CameraCapturer(Context context, IWatchLog log, Action<byte[]> onFrame, Action<string> onFailure)
    {
        _context = context;
        _log = log;
        _onFrame = onFrame;
        _onFailure = onFailure;
        _thread = new HandlerThread("VigiaHud.Camera");
        _thread.Start();
        _handler = new Handler(_thread.Looper!);
    }

    /// <summary>Opens the camera and starts the stream in the background.</summary>
    public void Open() => _handler.Post(OpenOnCameraThread);

    /// <summary>
    /// Asks for the next streamed frame. If the previous request is still waiting, the stream has stalled: that is
    /// reported as a failure, and the request stays pending for when frames come back.
    /// </summary>
    public void CaptureOne() => _handler.Post(() =>
    {
        if (_disposed)
        {
            return;
        }

        if (_session is null)
        {
            _onFailure("camera not ready");
            return;
        }

        if (_grabNext)
        {
            _onFailure("no frame from the camera since the last request");
            return;
        }

        _grabNext = true;
    });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handler.RemoveCallbacksAndMessages(null);
        _handler.Post(() =>
        {
            CloseOnCameraThread();
            _thread.QuitSafely();
        });
    }

    private void OpenOnCameraThread()
    {
        if (_disposed || _camera is not null)
        {
            return;
        }

        try
        {
            var manager = (CameraManager)_context.GetSystemService(Context.CameraService)!;
            var id = PickBackCamera(manager, out var characteristics);
            if (id is null)
            {
                Fail("no back camera found");
                return;
            }

            _size = PickSize(characteristics);
            _reader = ImageReader.NewInstance(_size.Width, _size.Height, ImageFormatType.Yuv420888, 2);
            _reader.SetOnImageAvailableListener(new ImageListener(this), _handler);
            _log.Write(LogKind.Info, $"camera {id} opening at {_size.Width}x{_size.Height}");
            manager.OpenCamera(id, new DeviceCallback(this), _handler);
        }
        catch (Exception error)
        {
            Fail($"camera open failed: {error.GetType().Name}: {error.Message}");
            ScheduleReopen();
        }
    }

    private void OnCameraOpened(CameraDevice camera)
    {
        if (_disposed)
        {
            camera.Close();
            return;
        }

        _camera = camera;
        try
        {
#pragma warning disable CA1422 // The SessionConfiguration overload is API 28+; this one still works and is simpler.
            camera.CreateCaptureSession(new List<Surface> { _reader!.Surface! }, new SessionCallback(this), _handler);
#pragma warning restore CA1422
        }
        catch (Exception error)
        {
            Fail($"session create failed: {error.GetType().Name}: {error.Message}");
            CloseOnCameraThread();
            ScheduleReopen();
        }
    }

    private void OnSessionConfigured(CameraCaptureSession session)
    {
        if (_disposed || _camera is null)
        {
            return;
        }

        try
        {
            var request = _camera.CreateCaptureRequest(CameraTemplate.Preview);
            request.AddTarget(_reader!.Surface!);
            // Never the flash: the watch must not light the room. The slowest frame rate the camera offers: less heat and power.
            request.Set(CaptureRequest.ControlAeMode!, (int)ControlAEMode.On);
            request.Set(CaptureRequest.FlashMode!, (int)FlashMode.Off);
            if (SlowestFpsRange(_camera.Id) is { } range)
            {
                request.Set(CaptureRequest.ControlAeTargetFpsRange!, range);
                _log.Write(LogKind.Info, $"camera ready, {range.Lower}-{range.Upper} fps");
            }
            else
            {
                _log.Write(LogKind.Info, "camera ready");
            }

            session.SetRepeatingRequest(request.Build(), null, _handler);
            _session = session;
        }
        catch (Exception error)
        {
            OnCameraLost($"stream start failed: {error.GetType().Name}: {error.Message}");
        }
    }

    private void OnImageAvailable(ImageReader reader)
    {
        Image? image = null;
        try
        {
            image = reader.AcquireLatestImage();
            if (image is null || !_grabNext)
            {
                return;
            }

            _grabNext = false;
            _onFrame(ToJpeg(image));
        }
        catch (Exception error)
        {
            Fail($"frame read failed: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            // Close(), not Dispose(): disposing only drops the JNI reference and the reader's slot stays taken
            // ("maxImages (2) has already been acquired", seen on the OPPO on 2026-10-22).
            image?.Close();
            image?.Dispose();
        }
    }

    private static byte[] ToJpeg(Image image)
    {
        var width = image.Width;
        var height = image.Height;
        var nv21 = ToNv21(image, width, height);
        using var yuv = new YuvImage(nv21, ImageFormatType.Nv21, width, height, null);
        using var output = new MemoryStream();
        yuv.CompressToJpeg(new Rect(0, 0, width, height), JpegQuality, output);
        return output.ToArray();
    }

    /// <summary>YUV_420_888 (planar or semi-planar, any strides) to NV21, the one layout <see cref="YuvImage"/> accepts.</summary>
    private static byte[] ToNv21(Image image, int width, int height)
    {
        var planes = image.GetPlanes()!;
        var nv21 = new byte[width * height * 3 / 2];

        var y = planes[0];
        var yBuffer = y.Buffer!;
        var yRowStride = y.RowStride;
        for (var row = 0; row < height; row++)
        {
            yBuffer.Position(row * yRowStride);
            yBuffer.Get(nv21, row * width, width);
        }

        var u = planes[1];
        var v = planes[2];
        var uBuffer = u.Buffer!;
        var vBuffer = v.Buffer!;
        var chromaHeight = height / 2;
        var chromaWidth = width / 2;
        var offset = width * height;
        for (var row = 0; row < chromaHeight; row++)
        {
            for (var col = 0; col < chromaWidth; col++)
            {
                nv21[offset++] = (byte)vBuffer.Get(row * v.RowStride + col * v.PixelStride);
                nv21[offset++] = (byte)uBuffer.Get(row * u.RowStride + col * u.PixelStride);
            }
        }

        return nv21;
    }

    private void OnCameraLost(string reason)
    {
        Fail(reason);
        CloseOnCameraThread();
        ScheduleReopen();
    }

    private void ScheduleReopen()
    {
        if (!_disposed)
        {
            _handler.PostDelayed(OpenOnCameraThread, (long)ReopenDelay.TotalMilliseconds);
        }
    }

    private void CloseOnCameraThread()
    {
        try { _session?.Close(); } catch (Exception) { /* already gone */ }
        try { _camera?.Close(); } catch (Exception) { /* already gone */ }
        try { _reader?.Close(); } catch (Exception) { /* already gone */ }
        _session = null;
        _camera = null;
        _reader = null;
        _grabNext = false;
    }

    private void Fail(string reason)
    {
        _log.Write(LogKind.Error, reason);
        _onFailure(reason);
    }

    private global::Android.Util.Range? SlowestFpsRange(string cameraId)
    {
        var manager = (CameraManager)_context.GetSystemService(Context.CameraService)!;
        var value = manager.GetCameraCharacteristics(cameraId).Get(CameraCharacteristics.ControlAeAvailableTargetFpsRanges);
        var ranges = value is null ? null : global::Android.Runtime.JNIEnv.GetArray<global::Android.Util.Range>(value.Handle);
        return ranges?
            .OrderBy(r => (int)r.Upper!)
            .ThenBy(r => (int)r.Lower!)
            .FirstOrDefault();
    }

    private static string? PickBackCamera(CameraManager manager, out CameraCharacteristics characteristics)
    {
        foreach (var id in manager.GetCameraIdList())
        {
            var candidate = manager.GetCameraCharacteristics(id);
            var facing = candidate.Get(CameraCharacteristics.LensFacing) as Integer;
            if (facing is not null && facing.IntValue() == (int)LensFacing.Back)
            {
                characteristics = candidate;
                return id;
            }
        }

        characteristics = null!;
        return null;
    }

    /// <summary>The smallest YUV size at least <see cref="MinWidth"/> wide; the smallest of all if none is that wide.</summary>
    private static Size PickSize(CameraCharacteristics characteristics)
    {
        var map = (StreamConfigurationMap)characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap)!;
        var sizes = map.GetOutputSizes((int)ImageFormatType.Yuv420888) ?? [];
        var ordered = sizes.OrderBy(s => s.Width * s.Height).ToList();
        return ordered.FirstOrDefault(s => s.Width >= MinWidth) ?? ordered.First();
    }

    private sealed class DeviceCallback(CameraCapturer owner) : CameraDevice.StateCallback
    {
        public override void OnOpened(CameraDevice camera) => owner.OnCameraOpened(camera);

        public override void OnDisconnected(CameraDevice camera) => owner.OnCameraLost("camera disconnected");

        public override void OnError(CameraDevice camera, CameraError error) => owner.OnCameraLost($"camera error {error}");
    }

    private sealed class SessionCallback(CameraCapturer owner) : CameraCaptureSession.StateCallback
    {
        public override void OnConfigured(CameraCaptureSession session) => owner.OnSessionConfigured(session);

        public override void OnConfigureFailed(CameraCaptureSession session) => owner.OnCameraLost("camera session configure failed");
    }

    private sealed class ImageListener(CameraCapturer owner) : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        public void OnImageAvailable(ImageReader? reader)
        {
            if (reader is not null)
            {
                owner.OnImageAvailable(reader);
            }
        }
    }
}
