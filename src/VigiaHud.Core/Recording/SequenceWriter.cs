using System.Buffers.Binary;
using System.IO.Compression;
using VigiaHud.Core.Motion;

namespace VigiaHud.Core.Recording;

/// <summary>
/// Writes a recording (<see cref="SequenceFormat"/>): the header at once, then one record per frame, each written in
/// one piece and flushed, so what is on disk is always whole frames, maybe followed by part of the last one.
/// </summary>
public sealed class SequenceWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly SequenceHeader _header;

    public SequenceWriter(Stream stream, SequenceHeader header)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(header);
        _stream = stream;
        _header = header;

        var json = SequenceFormat.HeaderJson(header);
        var start = new byte[SequenceFormat.Magic.Length + 1 + sizeof(int) + json.Length];
        SequenceFormat.Magic.CopyTo(start);
        start[SequenceFormat.Magic.Length] = SequenceFormat.Version;
        BinaryPrimitives.WriteInt32LittleEndian(start.AsSpan(SequenceFormat.Magic.Length + 1), json.Length);
        json.CopyTo(start, SequenceFormat.Magic.Length + 1 + sizeof(int));
        WriteFlushed(start);
    }

    /// <summary>Bytes written so far, header included.</summary>
    public long BytesWritten { get; private set; }

    public int FramesWritten { get; private set; }

    public void Write(DateTimeOffset at, LumaImage luma, ReadOnlySpan<byte> jpeg)
    {
        ArgumentNullException.ThrowIfNull(luma);
        if (luma.Width != _header.Width || luma.Height != _header.Height)
        {
            throw new ArgumentException($"The recording is {_header.Width}x{_header.Height}; this frame is {luma.Width}x{luma.Height}.", nameof(luma));
        }

        var deflated = Deflate(luma.Pixels);
        var length = sizeof(long) + sizeof(int) + deflated.Length + sizeof(int) + jpeg.Length;
        var record = new byte[sizeof(int) + length];
        var span = record.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, length);
        BinaryPrimitives.WriteInt64LittleEndian(span[4..], at.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], deflated.Length);
        deflated.CopyTo(span[16..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[(16 + deflated.Length)..], jpeg.Length);
        jpeg.CopyTo(span[(20 + deflated.Length)..]);
        WriteFlushed(record);
        FramesWritten++;
    }

    public void Dispose() => _stream.Dispose();

    private void WriteFlushed(byte[] bytes)
    {
        _stream.Write(bytes);
        _stream.Flush();
        BytesWritten += bytes.Length;
    }

    /// <summary>Lossless: the detector must be judged on exactly what it sees on the phone.</summary>
    private static byte[] Deflate(byte[] pixels)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(pixels);
        }

        return buffer.ToArray();
    }
}
