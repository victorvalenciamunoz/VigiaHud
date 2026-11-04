using System.Text;
using System.Text.Json;

namespace VigiaHud.Core.Recording;

/// <summary>
/// The layout of a recording file (DECISIONS 024), little-endian:
/// <list type="bullet">
/// <item>the magic <c>VSEQ</c> and a version byte;</item>
/// <item>the header as UTF-8 JSON, after its length (int32);</item>
/// <item>one record per frame, after its length (int32): the arrival instant (int64, Unix milliseconds UTC), the grey
/// picture deflated (int32 length and bytes) and the JPEG (int32 length and bytes).</item>
/// </list>
/// Each record goes to disk whole and flushed, so a process killed mid-recording leaves every earlier frame readable.
/// The header is JSON, read with <see cref="JsonDocument"/> and written with <see cref="Utf8JsonWriter"/>: no
/// reflection, safe in the trimmed app.
/// </summary>
internal static class SequenceFormat
{
    public static ReadOnlySpan<byte> Magic => "VSEQ"u8;

    public const byte Version = 1;

    /// <summary>A record longer than this is damage, not a frame: a 320x240 frame and its JPEG take some tens of KB.</summary>
    public const int MaxRecordBytes = 16 * 1024 * 1024;

    public static byte[] HeaderJson(SequenceHeader header)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("name", header.Name);
            json.WriteString("startedAt", header.StartedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
            json.WriteNumber("width", header.Width);
            json.WriteNumber("height", header.Height);
            json.WriteNumber("sourceWidth", header.SourceWidth);
            json.WriteNumber("sourceHeight", header.SourceHeight);
            json.WriteNumber("sensorOrientation", header.SensorOrientation);
            json.WriteNumber("frameIntervalMs", (long)header.FrameInterval.TotalMilliseconds);
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static SequenceHeader ParseHeader(ReadOnlySpan<byte> utf8)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray());
            var root = document.RootElement;
            return new SequenceHeader(
                root.GetProperty("name").GetString() ?? throw new InvalidDataException("The header has no name."),
                DateTimeOffset.Parse(root.GetProperty("startedAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                root.GetProperty("width").GetInt32(),
                root.GetProperty("height").GetInt32(),
                root.GetProperty("sourceWidth").GetInt32(),
                root.GetProperty("sourceHeight").GetInt32(),
                root.GetProperty("sensorOrientation").GetInt32(),
                TimeSpan.FromMilliseconds(root.GetProperty("frameIntervalMs").GetInt64()));
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"The header is not valid: {Encoding.UTF8.GetString(utf8)}", error);
        }
    }
}
