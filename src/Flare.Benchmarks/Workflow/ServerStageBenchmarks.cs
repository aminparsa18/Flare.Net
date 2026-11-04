using BenchmarkDotNet.Attributes;
using Flare.Api.Json;
using Microsoft.AspNetCore.Http;
using Flare.Benchmarks.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Flare.Benchmarks.Workflow;

/// <summary>
/// The server half of a dashboard request: the query result object goes through the production
/// <see cref="ApiSerialization.Write{T}"/> (content negotiation on <c>Accept</c>), the resulting
/// <see cref="IResult"/> executes against an in-memory response, and the body is optionally
/// compressed on the way out - the same stream wrapping <c>UseResponseCompression</c> does.
/// </summary>
/// <remarks>
/// The report splits this into stages: encode = the <c>none</c> case, compress = this case minus
/// <c>none</c>. Querying ClickHouse is excluded: it is identical for both formats.
/// </remarks>
[MemoryDiagnoser]
public class ServerStageBenchmarks
{
    private Scenario _scenario = null!;
    private IServiceProvider _services = null!;
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
        _services = new ServiceCollection().AddLogging().BuildServiceProvider();
        _body = new MemoryStream(_scenario.EncodeJson().Length * 2);
    }

    [Benchmark]
    public async Task<long> Respond()
    {
        _body.SetLength(0);
        var http = new DefaultHttpContext { RequestServices = _services };
        http.Request.Headers.Accept = Format == "json" ? "application/json" : ApiSerialization.MemoryPackContentType;

        var target = Compression.Wrap(_body, Codec);
        http.Response.Body = target;
        await _scenario.Write(http).ExecuteAsync(http);
        if (!ReferenceEquals(target, _body))
        {
            await target.DisposeAsync();
        }

        return _body.Length;
    }
}
