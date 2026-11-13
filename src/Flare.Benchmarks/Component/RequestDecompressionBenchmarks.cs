using BenchmarkDotNet.Attributes;
using Flare.Benchmarks.Fixtures.Synthetic;
using Flare.Ingest.Otlp;
using Grpc.Net.Compression;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Flare.Benchmarks.Component;

/// <summary>
/// Flare.Ingest decompressing an OTLP export request (<see cref="OtlpRequestFixtures"/>), gzip vs
/// zstd, through the receiver's own <c>grpc-encoding</c> providers
/// (<see cref="ZstandardGrpcCompressionProvider.ReceiverProviders"/>). The HTTP receiver's
/// <c>Content-Encoding</c> providers wrap the same <c>GZipStream</c>/<c>ZstandardStream</c>.
/// <c>none</c> is the plain copy every request pays anyway. Compressed sizes are in the fixture
/// manifest (<c>otlpRequests</c>).
/// </summary>
[MemoryDiagnoser]
public class RequestDecompressionBenchmarks
{
    private static readonly Dictionary<string, ICompressionProvider> Providers =
        ZstandardGrpcCompressionProvider.ReceiverProviders().ToDictionary(p => p.EncodingName);

    private byte[] _body = null!;
    private ICompressionProvider? _provider;

    [ParamsSource(nameof(RequestNames))]
    public string Request { get; set; } = string.Empty;

    [ParamsSource(nameof(Codecs))]
    public string Codec { get; set; } = string.Empty;

    public static IEnumerable<string> RequestNames => OtlpRequestFixtures.Names;

    public static IEnumerable<string> Codecs => OtlpRequestFixtures.Codecs;

    [GlobalSetup]
    public void Setup()
    {
        _body = OtlpRequestFixtures.Compress(OtlpRequestFixtures.Encode(Request), Codec);
        _provider = Codec == OtlpRequestFixtures.None ? null : Providers[OtlpRequestFixtures.EncodingName(Codec)];
    }

    /// <summary>Compressed body → decompressed bytes in memory, as both receivers buffer it.</summary>
    [Benchmark]
    public long Decompress() => DecompressToMemory().Length;

    /// <summary><see cref="Decompress"/> plus the protobuf parse that follows it, for scale.</summary>
    [Benchmark]
    public int DecompressAndParse()
    {
        using var buffer = DecompressToMemory();
        buffer.Position = 0;
        return Request == "logs"
            ? ExportLogsServiceRequest.Parser.ParseFrom(buffer).ResourceLogs.Count
            : ExportTraceServiceRequest.Parser.ParseFrom(buffer).ResourceSpans.Count;
    }

    private MemoryStream DecompressToMemory()
    {
        var output = new MemoryStream();
        using var input = new MemoryStream(_body, writable: false);
        if (_provider is null)
        {
            input.CopyTo(output);
            return output;
        }

        using (var decompress = _provider.CreateDecompressionStream(input))
        {
            decompress.CopyTo(output);
        }

        return output;
    }
}
