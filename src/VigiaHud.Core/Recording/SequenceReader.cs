using System.Buffers.Binary;
using System.IO.Compression;
using VigiaHud.Core.Motion;

namespace VigiaHud.Core.Recording;

/// <summary>
/// Reads a recording (<see cref="SequenceFormat"/>) frame by frame, so a night of frames never sits in memory at once.
/// A file that ends partway through a frame, as when Android kills the app mid-recording, reads up to its last whole
/// frame and says so in <see cref="Truncated"/>. Damage anywhere else is <see cref="InvalidDataException"/>.
/// </summary>
public sealed class SequenceReader : IDisposable
{
    private readonly Stream _stream;

    public SequenceReader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;

        var start = new byte[SequenceFormat.Magic.Length + 1 + sizeof(int)];
        if (!TryFill(start) || !start.AsSpan(0, SequenceFormat.Magic.Length).SequenceEqual(SequenceFormat.Magic))
        {
            throw new InvalidDataException("Not a VigiaHud recording.");
        }

        var version = start[SequenceFormat.Magic.Length];
        if (version != SequenceFormat.Version)
        {
            throw new InvalidDataException($"Recording format version {version}; this code reads version {SequenceFormat.Version}.");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(start.AsSpan(SequenceFormat.Magic.Length + 1));
        if (length <= 0 || length > SequenceFormat.MaxRecordBytes)
        {
            throw new InvalidDataException($"Header length {length} is not valid.");
        }

        var json = new byte[length];
        if (!TryFill(json))
        {
            throw new InvalidDataException("The recording ends inside its header.");
        }

        Header = SequenceFormat.ParseHeader(json);
    }

    public SequenceHeader Header { get; }

    /// <summary>True once reading has hit the end partway through a frame.</summary>
    public bool Truncated { get; private set; }

    /// <summary>The frames in order, read as they are asked for; enumerate once.</summary>
    public IEnumerable<SequenceFrame> ReadFrames()
    {
        var lengthBytes = new byte[sizeof(int)];
        for (var index = 0; ; index++)
        {
            var got = _stream.ReadAtLeast(lengthBytes, lengthBytes.Length, throwOnEndOfStream: false);
            if (got == 0)
            {
                yield break;
            }

            if (got < lengthBytes.Length)
            {
                Truncated = true;
                yield break;
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (length < sizeof(long) + (2 * sizeof(int)) || length > SequenceFormat.MaxRecordBytes)
            {
                throw new InvalidDataException($"Frame {index}: record length {length} is not valid.");
            }

            var record = new byte[length];
            if (!TryFill(record))
            {
                Truncated = true;
                yield break;
            }

            yield return Parse(record, index);
        }
    }

    public void Dispose() => _stream.Dispose();

    private SequenceFrame Parse(byte[] record, int index)
    {
        var span = record.AsSpan();
        var at = DateTimeOffset.FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64LittleEndian(span));
        var lumaLength = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
        if (lumaLength < 0 || 12 + lumaLength + sizeof(int) > record.Length)
        {
            throw new InvalidDataException($"Frame {index}: grey picture length {lumaLength} does not fit its record.");
        }

        var jpegLength = BinaryPrimitives.ReadInt32LittleEndian(span[(12 + lumaLength)..]);
        if (jpegLength < 0 || 16 + lumaLength + jpegLength != record.Length)
        {
            throw new InvalidDataException($"Frame {index}: JPEG length {jpegLength} does not fit its record.");
        }

        var pixels = Inflate(record, 12, lumaLength, Header.Width * Header.Height, index);
        var jpeg = span.Slice(16 + lumaLength, jpegLength).ToArray();
        return new SequenceFrame(at, new LumaImage(Header.Width, Header.Height, pixels), jpeg);
    }

    private static byte[] Inflate(byte[] record, int offset, int length, int expected, int index)
    {
        var pixels = new byte[expected];
        try
        {
            using var deflate = new DeflateStream(new MemoryStream(record, offset, length), CompressionMode.Decompress);
            var got = deflate.ReadAtLeast(pixels, expected, throwOnEndOfStream: false);
            if (got == expected && deflate.ReadByte() == -1)
            {
                return pixels;
            }
        }
        catch (InvalidDataException)
        {
            // Reported below with the frame number.
        }

        throw new InvalidDataException($"Frame {index}: the grey picture is damaged or not {expected} pixels.");
    }

    private bool TryFill(byte[] buffer) =>
        _stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
}
