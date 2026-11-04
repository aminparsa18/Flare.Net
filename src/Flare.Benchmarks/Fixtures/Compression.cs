using System.IO.Compression;

namespace Flare.Benchmarks.Fixtures;

/// <summary>
/// The HTTP <c>Content-Encoding</c> settings under comparison. Levels are explicit numbers rather
/// than <see cref="CompressionLevel"/> so the result names mean the same thing on every runtime:
/// on .NET 10, <c>Fastest</c> is gzip level 1, <c>Optimal</c> is gzip level 6 and Brotli quality 4,
/// so these are also the settings <c>UseResponseCompression</c> would pick.
/// </summary>
public static class Compression
{
    public const string None = "none";

    /// <summary>Every codec including <see cref="None"/>.</summary>
    public static readonly string[] All = [None, "gzip-1", "gzip-6", "brotli-4"];

    /// <summary>Every codec that actually compresses.</summary>
    public static readonly string[] Compressing = ["gzip-1", "gzip-6", "brotli-4"];

    /// <summary>Wraps <paramref name="inner"/> in a compressing stream (left open on dispose), or returns it as-is for <see cref="None"/>.</summary>
    public static Stream Wrap(Stream inner, string codec) => codec switch
    {
        None => inner,
        "gzip-1" => new GZipStream(inner, new ZLibCompressionOptions { CompressionLevel = 1 }, leaveOpen: true),
        "gzip-6" => new GZipStream(inner, new ZLibCompressionOptions { CompressionLevel = 6 }, leaveOpen: true),
        "brotli-4" => new BrotliStream(inner, new BrotliCompressionOptions { Quality = 4 }, leaveOpen: true),
        _ => throw new ArgumentException($"Unknown codec '{codec}'."),
    };

    public static byte[] Compress(byte[] data, string codec)
    {
        if (codec == None)
        {
            return data;
        }

        using var output = new MemoryStream(data.Length / 2);
        using (var stream = Wrap(output, codec))
        {
            stream.Write(data);
        }

        return output.ToArray();
    }
}
