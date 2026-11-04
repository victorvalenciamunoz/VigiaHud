using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Views;
using Java.Lang;
using VigiaHud.Core.Logging;
using VigiaHud.Core.Recording;
using Exception = System.Exception;
using Image = Android.Media.Image;
using Size = Android.Util.Size;
using Rect = Android.Graphics.Rect;

namespace VigiaHud.App.Platforms.Android.Watch;

/// <summary>
/// The back camera streaming at low resolution and the lowest frame rate it offers, with Camera2 on its own thread,
/// and one frame handed out as a JPEG each time <see cref="CaptureOne"/> asks. A stream rather than single stills
/// because a still from an idle session comes out black (exposure never converges; seen on the OPPO on 2026-10-22),
/// and because phase 3 will want frames continuously anyway. If Android takes the camera away (another app opens it),
/// it is reopened as soon as the camera service says it is free again, with a timed fallback, and the CPU is held
/// awake meanwhile (DECISIONS 013): a streaming camera keeps the phone awake on its own, a lost one does not, and a
/// timer on a sleeping CPU does not tick (15 min lost on the OPPO on 2026-10-26). Every failure goes to the log and to
/// <c>onFailure</c>; nothing here throws into the service.
/// </summary>
public sealed class CameraCapturer : IDisposable
{
    private const int MinWidth = 320;
    private const int JpegQuality = 70;
    private static readonly TimeSpan ReopenDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RecoveryHold = TimeSpan.FromMinutes(2);

    private readonly Context _context;
    private readonly IWatchLog _log;
    private readonly Action<byte[]> _onFrame;
    private readonly Action<string> _onFailure;
    private readonly Recorder? _recorder;
    private readonly HandlerThread _thread;
    private readonly Handler _handler;
    private readonly CameraManager _manager;
    private readonly AvailabilityListener _availability;
    private readonly Java.Lang.Runnable _reopen;
    private readonly PowerManager.WakeLock _recovery;

    /// <summary>The size of the photo the owner asks for (DECISIONS 021): the smallest JPEG at least this wide.</summary>
    private const int StillMinWidth = 1280;
    private const int StillJpegQuality = 85;

    private CameraDevice? _camera;
    private CameraCaptureSession? _session;
    private ImageReader? _reader;
    private ImageReader? _stillReader;
    private TaskCompletionSource<byte[]?>? _still;
    private Size _size = new(MinWidth, MinWidth * 3 / 4);
    private string? _cameraId;
    private bool _grabNext;
    private bool _opening;
    private bool _lost;
    private bool _disposed;

    /// <param name="recorder">Test recordings (DECISIONS 024): offered every streamed frame, it keeps one a second while recording.</param>
    public CameraCapturer(Context context, IWatchLog log, Action<byte[]> onFrame, Action<string> onFailure, Recorder? recorder = null)
    {
        _context = context;
        _log = log;
        _onFrame = onFrame;
        _onFailure = onFailure;
        _recorder = recorder;
        _thread = new HandlerThread("VigiaHud.Camera");
        _thread.Start();
        _handler = new Handler(_thread.Looper!);
        _reopen = new Java.Lang.Runnable(OpenOnCameraThread);
        _manager = (CameraManager)context.GetSystemService(Context.CameraService)!;
        _availability = new AvailabilityListener(this);
        _manager.RegisterAvailabilityCallback(_availability, _handler);
        var power = (PowerManager)context.GetSystemService(Context.PowerService)!;
        _recovery = power.NewWakeLock(WakeLockFlags.Partial, "VigiaHud::CameraRecovery")!;
        _recovery.SetReferenceCounted(false);
    }

    /// <summary>How the back camera's sensor is mounted, clockwise from the phone upright (90 on the OPPO).</summary>
    public int SensorOrientation { get; private set; } = 90;

    /// <summary>Opens the camera and starts the stream in the background.</summary>
    public void Open() => _handler.Post(OpenOnCameraThread);

    /// <summary>
    /// For the watchdog: if the camera is lost and not being reopened, try now instead of waiting for a timer that
    /// may not have ticked while the CPU slept.
    /// </summary>
    public void EnsureStreaming() => _handler.Post(() =>
    {
        if (_disposed || !_lost || _opening || _camera is not null)
        {
            return;
        }

        _log.Write(LogKind.Info, "watchdog: camera still lost; reopening now");
        _handler.RemoveCallbacks(_reopen);
        OpenOnCameraThread();
    });

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

    /// <summary>
    /// A photo for the owner, larger than the stream frames: one capture on the running session, so exposure is already
    /// right, to a JPEG output that the stream never uses. <paramref name="jpegOrientation"/> (0, 90, 180, 270) is how
    /// much the picture must turn to stand upright; the camera writes it into the JPEG. Null if the camera is not
    /// streaming or the capture fails; the caller falls back to a stream frame.
    /// </summary>
    public Task<byte[]?> TakeStillAsync(int jpegOrientation)
    {
        var done = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Post(() =>
        {
            if (_disposed || _session is null || _camera is null || _stillReader is null)
            {
                done.TrySetResult(null);
                return;
            }

            if (_still is { } pending)
            {
                // One at a time: a second request gets the same photo.
                pending.Task.ContinueWith(t => done.TrySetResult(t.Result), TaskScheduler.Default);
                return;
            }

            try
            {
                var request = _camera.CreateCaptureRequest(CameraTemplate.StillCapture);
                request.AddTarget(_stillReader.Surface!);
                request.Set(CaptureRequest.ControlAeMode!, (int)ControlAEMode.On);
                request.Set(CaptureRequest.FlashMode!, (int)FlashMode.Off);
                request.Set(CaptureRequest.JpegOrientation!, Java.Lang.Integer.ValueOf(jpegOrientation));
                request.Set(CaptureRequest.JpegQuality!, Java.Lang.Byte.ValueOf((sbyte)StillJpegQuality));
                _still = done;
                _session.Capture(request.Build(), null, _handler);
            }
            catch (Exception error)
            {
                _still = null;
                _log.Write(LogKind.Error, $"photo capture failed: {error.GetType().Name}: {error.Message}");
                done.TrySetResult(null);
            }
        });
        return done.Task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handler.RemoveCallbacksAndMessages(null);
        try { _manager.UnregisterAvailabilityCallback(_availability); } catch (Exception) { /* already gone */ }
        _handler.Post(() =>
        {
            CloseOnCameraThread();
            ReleaseRecoveryLock();
            _thread.QuitSafely();
        });
    }

    private void OpenOnCameraThread()
    {
        if (_disposed || _opening || _camera is not null)
        {
            return;
        }

        try
        {
            var id = PickBackCamera(_manager, out var characteristics);
            if (id is null)
            {
                Fail("no back camera found");
                return;
            }

            _cameraId = id;
            if (characteristics.Get(CameraCharacteristics.SensorOrientation) is Integer mounting)
            {
                SensorOrientation = mounting.IntValue();
            }

            _size = PickSize(characteristics);
            _reader = ImageReader.NewInstance(_size.Width, _size.Height, ImageFormatType.Yuv420888, 2);
            _reader.SetOnImageAvailableListener(new ImageListener(this), _handler);
            var still = PickStillSize(characteristics);
            _stillReader = ImageReader.NewInstance(still.Width, still.Height, ImageFormatType.Jpeg, 1);
            _stillReader.SetOnImageAvailableListener(new StillListener(this), _handler);
            _log.Write(LogKind.Info, $"camera {id} opening at {_size.Width}x{_size.Height}, photos at {still.Width}x{still.Height}");
            _opening = true;
            _manager.OpenCamera(id, new DeviceCallback(this), _handler);
        }
        catch (CameraAccessException busy) when (busy.Reason == CameraAccessErrorType.InUse)
        {
            OnCameraBusy();
        }
        catch (Exception error)
        {
            _opening = false;
            Fail($"camera open failed: {error.GetType().Name}: {error.Message}");
            CloseOnCameraThread();
            MarkLost();
        }
    }

    private void OnCameraOpened(CameraDevice camera)
    {
        _opening = false;
        if (_disposed)
        {
            camera.Close();
            return;
        }

        _camera = camera;
        try
        {
#pragma warning disable CA1422 // The SessionConfiguration overload is API 28+; this one still works and is simpler.
            camera.CreateCaptureSession(new List<Surface> { _reader!.Surface!, _stillReader!.Surface! }, new SessionCallback(this), _handler);
#pragma warning restore CA1422
        }
        catch (Exception error)
        {
            Fail($"session create failed: {error.GetType().Name}: {error.Message}");
            CloseOnCameraThread();
            MarkLost();
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
            // Back: the stream keeps the phone awake from here on, and the timed fallback is no longer needed.
            _lost = false;
            _handler.RemoveCallbacks(_reopen);
            ReleaseRecoveryLock();
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
            if (image is null)
            {
                return;
            }

            // Asked every frame (15 a second), but it only says yes once a second while a test recording runs.
            var record = _recorder?.WantsFrame() == true;
            if (!_grabNext && !record)
            {
                return;
            }

            // One copy and one JPEG serve both the watch and the recording.
            var frame = Planes.Copy(image);
            var jpeg = ToJpeg(frame);
            if (record)
            {
                _recorder!.Write(frame.Y, frame.Width, frame.Height, frame.YRowStride, SensorOrientation, jpeg);
            }

            if (_grabNext)
            {
                _grabNext = false;
                _onFrame(jpeg);
            }
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

    private static byte[] ToJpeg(Planes frame)
    {
        var nv21 = ToNv21(frame);
        using var yuv = new YuvImage(nv21, ImageFormatType.Nv21, frame.Width, frame.Height, null);
        using var output = new MemoryStream();
        yuv.CompressToJpeg(new Rect(0, 0, frame.Width, frame.Height), JpegQuality, output);
        return output.ToArray();
    }

    /// <summary>
    /// YUV_420_888 (planar or semi-planar, any strides) to NV21, the one layout <see cref="YuvImage"/> accepts, read
    /// from the planes already copied out of Java.
    /// </summary>
    private static byte[] ToNv21(Planes frame)
    {
        var (width, height) = (frame.Width, frame.Height);
        var nv21 = new byte[width * height * 3 / 2];
        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(frame.Y, row * frame.YRowStride, nv21, row * width, width);
        }

        var chromaHeight = height / 2;
        var chromaWidth = width / 2;
        var offset = width * height;
        for (var row = 0; row < chromaHeight; row++)
        {
            for (var col = 0; col < chromaWidth; col++)
            {
                nv21[offset++] = frame.V[row * frame.VRowStride + col * frame.VPixelStride];
                nv21[offset++] = frame.U[row * frame.URowStride + col * frame.UPixelStride];
            }
        }

        return nv21;
    }

    /// <summary>
    /// The three planes of a frame copied into managed memory, each crossing from Java once in one copy: reading them
    /// pixel by pixel through the buffer cost about 38 000 calls into Java per frame and 41 % of a core on the OPPO
    /// (2026-11-03, DECISIONS 021). The Y plane is kept with its row stride, as the recorder reads it.
    /// </summary>
    private sealed record Planes(
        int Width, int Height,
        byte[] Y, int YRowStride,
        byte[] U, int URowStride, int UPixelStride,
        byte[] V, int VRowStride, int VPixelStride)
    {
        public static Planes Copy(Image image)
        {
            var planes = image.GetPlanes()!;
            var (y, u, v) = (planes[0], planes[1], planes[2]);
            return new Planes(
                image.Width, image.Height,
                CameraCapturer.Copy(y.Buffer!), y.RowStride,
                CameraCapturer.Copy(u.Buffer!), u.RowStride, u.PixelStride,
                CameraCapturer.Copy(v.Buffer!), v.RowStride, v.PixelStride);
        }
    }

    /// <summary>The whole remaining buffer in one call into Java.</summary>
    private static byte[] Copy(Java.Nio.ByteBuffer buffer)
    {
        buffer.Rewind();
        var bytes = new byte[buffer.Remaining()];
        buffer.Get(bytes);
        return bytes;
    }

    private void OnCameraLost(string reason)
    {
        _opening = false;
        Fail(reason);
        CloseOnCameraThread();
        MarkLost();
    }

    /// <summary>Opening raced another app for the camera and lost: not a fault, just wait for it to be free.</summary>
    private void OnCameraBusy()
    {
        _opening = false;
        _log.Write(LogKind.Info, "camera busy with another app; waiting");
        CloseOnCameraThread();
        MarkLost();
    }

    /// <summary>
    /// The camera is gone: keep the CPU awake for a while (without the stream nothing else does), and reopen when the
    /// camera service says it is free, or after <see cref="ReopenDelay"/> at the latest.
    /// </summary>
    private void MarkLost()
    {
        if (_disposed)
        {
            return;
        }

        _lost = true;
        _recovery.Acquire((long)RecoveryHold.TotalMilliseconds);
        _handler.RemoveCallbacks(_reopen);
        _handler.PostDelayed(_reopen, (long)ReopenDelay.TotalMilliseconds);
    }

    private void OnCameraAvailable(string cameraId)
    {
        if (_disposed || !_lost || _opening || _camera is not null || cameraId != _cameraId)
        {
            return;
        }

        _log.Write(LogKind.Info, $"camera {cameraId} free again; reopening");
        _handler.RemoveCallbacks(_reopen);
        OpenOnCameraThread();
    }

    private void ReleaseRecoveryLock()
    {
        try
        {
            if (_recovery.IsHeld)
            {
                _recovery.Release();
            }
        }
        catch (Exception) { /* timed out between the check and the release */ }
    }

    private void CloseOnCameraThread()
    {
        try { _session?.Close(); } catch (Exception) { /* already gone */ }
        try { _camera?.Close(); } catch (Exception) { /* already gone */ }
        try { _reader?.Close(); } catch (Exception) { /* already gone */ }
        try { _stillReader?.Close(); } catch (Exception) { /* already gone */ }
        _session = null;
        _camera = null;
        _reader = null;
        _stillReader = null;
        _grabNext = false;
        _still?.TrySetResult(null);
        _still = null;
    }

    private void OnStillAvailable(ImageReader reader)
    {
        Image? image = null;
        try
        {
            image = reader.AcquireNextImage();
            if (image is null)
            {
                return;
            }

            var jpeg = Copy(image.GetPlanes()![0].Buffer!);
            _still?.TrySetResult(jpeg);
        }
        catch (Exception error)
        {
            _log.Write(LogKind.Error, $"photo read failed: {error.GetType().Name}: {error.Message}");
            _still?.TrySetResult(null);
        }
        finally
        {
            // No-op if already answered; otherwise the caller hears "no photo" now, not at its timeout.
            _still?.TrySetResult(null);
            _still = null;
            image?.Close();
            image?.Dispose();
        }
    }

    /// <summary>The smallest JPEG size at least <see cref="StillMinWidth"/> wide with the stream's 4:3 shape; else the closest.</summary>
    private static Size PickStillSize(CameraCharacteristics characteristics)
    {
        var map = (StreamConfigurationMap)characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap)!;
        var sizes = (map.GetOutputSizes((int)ImageFormatType.Jpeg) ?? []).OrderBy(s => s.Width * s.Height).ToList();
        return sizes.FirstOrDefault(s => s.Width >= StillMinWidth && s.Width * 3 == s.Height * 4)
            ?? sizes.FirstOrDefault(s => s.Width >= StillMinWidth)
            ?? sizes.Last();
    }

    private void Fail(string reason)
    {
        _log.Write(LogKind.Error, reason);
        _onFailure(reason);
    }

    private global::Android.Util.Range? SlowestFpsRange(string cameraId)
    {
        var value = _manager.GetCameraCharacteristics(cameraId).Get(CameraCharacteristics.ControlAeAvailableTargetFpsRanges);
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

        public override void OnError(CameraDevice camera, CameraError error)
        {
            if (error == CameraError.CameraInUse)
            {
                owner.OnCameraBusy();
            }
            else
            {
                owner.OnCameraLost($"camera error {error}");
            }
        }
    }

    private sealed class SessionCallback(CameraCapturer owner) : CameraCaptureSession.StateCallback
    {
        public override void OnConfigured(CameraCaptureSession session) => owner.OnSessionConfigured(session);

        public override void OnConfigureFailed(CameraCaptureSession session) => owner.OnCameraLost("camera session configure failed");
    }

    /// <summary>The camera service telling which cameras are free; how the watch hears another app let go of the camera.</summary>
    private sealed class AvailabilityListener(CameraCapturer owner) : CameraManager.AvailabilityCallback
    {
        public override void OnCameraAvailable(string cameraId) => owner.OnCameraAvailable(cameraId);
    }

    private sealed class StillListener(CameraCapturer owner) : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        public void OnImageAvailable(ImageReader? reader)
        {
            if (reader is not null)
            {
                owner.OnStillAvailable(reader);
            }
        }
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
