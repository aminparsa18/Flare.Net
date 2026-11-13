using System.IO.Compression;
using Grpc.Net.Compression;

namespace Flare.Ingest.Otlp;

/// <summary>
/// The <c>zstd</c> <c>grpc-encoding</c> for the OTLP gRPC receiver, over .NET 11's built-in
/// <see cref="ZstandardStream"/>. Grpc.Net ships only gzip and deflate providers; the Collector's
/// <c>otlp</c> exporter offers <c>compression: zstd</c>, and without this provider such a request
/// fails with <c>UNIMPLEMENTED</c>.
/// </summary>
/// <remarks>
/// The decompressed size needs no cap here: Grpc.AspNetCore reads a provider's decompression
/// stream with <c>MaxReceiveMessageSize</c> as the limit and rejects a larger message with
/// <c>RESOURCE_EXHAUSTED</c>, whatever the encoding.
/// </remarks>
public sealed class ZstandardGrpcCompressionProvider : ICompressionProvider
{
    public string EncodingName => "zstd";

    public Stream CreateCompressionStream(Stream stream, CompressionLevel? compressionLevel) =>
        new ZstandardStream(stream, compressionLevel ?? CompressionLevel.Fastest, leaveOpen: true);

    public Stream CreateDecompressionStream(Stream stream) =>
        new ZstandardStream(stream, CompressionMode.Decompress, leaveOpen: true);

    /// <summary>
    /// Every <c>grpc-encoding</c> the receiver accepts. Setting <c>GrpcServiceOptions.CompressionProviders</c>
    /// replaces Grpc.AspNetCore's gzip/deflate defaults rather than adding to them, so they are listed again.
    /// </summary>
    public static IList<ICompressionProvider> ReceiverProviders() =>
    [
        new GzipCompressionProvider(CompressionLevel.Fastest),
        new DeflateCompressionProvider(CompressionLevel.Fastest),
        new ZstandardGrpcCompressionProvider(),
    ];
}
