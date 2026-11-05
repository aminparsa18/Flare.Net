using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using Flare.Api.Hosting;
using Flare.Api.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Flare.Benchmarks.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Flare.Benchmarks.Workflow;

/// <summary>
/// The server half of a dashboard request: the query result object goes through the production
/// <see cref="ApiSerialization.Write{T}"/> (content negotiation on <c>Accept</c>), the resulting
/// <see cref="IResult"/> executes against an in-memory response, and compressed cases run inside the
/// real <see cref="ResponseCompressionMiddleware"/>.
/// </summary>
/// <remarks>
/// gzip-1 is Flare.Api's shipped configuration, registered by the same
/// <see cref="FlareResponseCompression.AddFlareResponseCompression"/> call (ADR-0277); gzip-6 is its
/// <c>ResponseCompression:Level=Optimal</c>. brotli-4 is the framework's Brotli provider at
/// <see cref="CompressionLevel.Optimal"/>, which Flare does not register; it stays for comparison.
/// On .NET 10 these <see cref="CompressionLevel"/>s are exactly the numeric levels
/// <see cref="Compression"/> uses for the fixture sizes, which <see cref="Setup"/> checks.
/// <para/>
/// The report splits this into stages: encode = the <c>none</c> case, compress = this case minus
/// <c>none</c>. Querying ClickHouse is excluded: it is identical for both formats.
/// </remarks>
[MemoryDiagnoser]
public class ServerStageBenchmarks
{
    private Scenario _scenario = null!;
    private IServiceProvider _services = null!;
    private RequestDelegate _pipeline = null!;
    private string? _acceptEncoding;
    private MemoryStream _body = null!;

    [ParamsSource(nameof(ScenarioNames))]
    public string Scenario { get; set; } = string.Empty;

    [Params("json", "memorypack")]
    public string Format { get; set; } = string.Empty;

    [ParamsSource(nameof(Codecs))]
    public string Codec { get; set; } = string.Empty;

    public static IEnumerable<string> ScenarioNames => Scenarios.Names;

    public static IEnumerable<string> Codecs => Compression.All;

    [GlobalSetup]
    public void Setup()
    {
        _scenario = Scenarios.Get(Scenario);
        var services = new ServiceCollection().AddLogging();
        switch (Codec)
        {
            case Compression.None:
                break;
            case "gzip-1":
                services.AddFlareResponseCompression(new ResponseCompressionSettings());
                break;
            case "gzip-6":
                services.AddFlareResponseCompression(new ResponseCompressionSettings { Level = CompressionLevel.Optimal });
                break;
            case "brotli-4":
                services.AddResponseCompression(o =>
                {
                    o.EnableForHttps = true;
                    o.MimeTypes = FlareResponseCompression.MimeTypes;
                    o.Providers.Add<BrotliCompressionProvider>();
                });
                services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
                break;
            default:
                throw new ArgumentException($"Unknown codec '{Codec}'.");
        }

        _services = services.BuildServiceProvider();
        RequestDelegate endpoint = http => _scenario.Write(http).ExecuteAsync(http);
        _pipeline = Codec == Compression.None
            ? endpoint
            : new ResponseCompressionMiddleware(endpoint, _services.GetRequiredService<IResponseCompressionProvider>()).Invoke;
        _acceptEncoding = Codec == Compression.None ? null : "gzip, deflate, br, zstd";
        _body = new MemoryStream(_scenario.EncodeJson().Length * 2);

        // The middleware must really have compressed, at the level the fixture sizes were measured at.
        // Not byte-exact: Results.Json flushes mid-body, and the middleware turns each flush into a
        // deflate/Brotli flush block, so JSON bodies come out a few percent larger than the one-shot
        // fixture (the report's transfer model uses the fixture size).
        var expected = Compression.Compress(Format == "json" ? _scenario.EncodeJson() : _scenario.EncodeMemoryPack(), Codec).Length;
        var actual = Respond().GetAwaiter().GetResult();
        Console.WriteLine($"// size-check {Scenario}/{Format}/{Codec}: middleware {actual} B, fixture {expected} B ({(double)actual / expected - 1:+0.0%;-0.0%})");
        if (Math.Abs(actual - expected) > expected * 0.10 + 16)
        {
            throw new InvalidOperationException($"{Scenario}/{Format}/{Codec}: middleware wrote {actual} bytes, fixture is {expected}.");
        }
    }

    [Benchmark]
    public async Task<long> Respond()
    {
        _body.SetLength(0);
        var http = new DefaultHttpContext { RequestServices = _services };
        http.Request.Headers.Accept = Format == "json" ? "application/json" : ApiSerialization.MemoryPackContentType;
        if (_acceptEncoding is not null)
        {
            http.Request.Headers.AcceptEncoding = _acceptEncoding;
        }

        http.Response.Body = _body;
        await _pipeline(http);
        return _body.Length;
    }
}
