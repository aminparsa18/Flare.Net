using System.IO.Compression;
using Flare.Ingest.Otlp;
using Xunit;

namespace Flare.Ingest.Tests.Otlp;

public class ZstandardGrpcCompressionProviderTests
{
    private static readonly byte[] Payload = [.. Enumerable.Range(0, 64 * 1024).Select(i => (byte)(i % 251))];

    [Fact]
    public void DecompressionStream_ReadsWhatAZstdSenderWrote()
    {
        using var compressed = new MemoryStream();
        using (var zstd = new ZstandardStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zstd.Write(Payload);
        }

        compressed.Position = 0;
        using var output = new MemoryStream();
        using (var decompress = new ZstandardGrpcCompressionProvider().CreateDecompressionStream(compressed))
        {
            decompress.CopyTo(output);
        }

        Assert.Equal(Payload, output.ToArray());
    }

    [Fact]
    public void CompressionStream_RoundTripsAndLeavesTheTargetOpen()
    {
        var provider = new ZstandardGrpcCompressionProvider();
        using var compressed = new MemoryStream();
        using (var compress = provider.CreateCompressionStream(compressed, compressionLevel: null))
        {
            compress.Write(Payload);
        }

        Assert.True(compressed.CanRead);
        Assert.True(compressed.Length < Payload.Length);

        compressed.Position = 0;
        using var output = new MemoryStream();
        using (var decompress = provider.CreateDecompressionStream(compressed))
        {
            decompress.CopyTo(output);
        }

        Assert.Equal(Payload, output.ToArray());
    }

    [Fact]
    public void ReceiverProviders_KeepGrpcDefaultsAndAddZstd()
    {
        var names = ZstandardGrpcCompressionProvider.ReceiverProviders().Select(p => p.EncodingName);

        Assert.Equal(["gzip", "deflate", "zstd"], names);
    }
}
