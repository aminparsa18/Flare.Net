using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Benchmarks.TestData;
using MemoryPack;

namespace Flare.Benchmarks.Benchmarks;

/// <summary>
/// <see cref="Flare.Api.Json.ApiSerialization"/>'s MemoryPack path (ADR-0015) vs its JSON
/// default (<see cref="LogsJsonContext"/>, camelCase + string enums, same as every other
/// Minimal API endpoint) for <c>POST /api/logs/search</c>'s response shape. See
/// docs-internal/planning/roadmap.md's "Flare-specific JSON-vs-MemoryPack benchmark" item.
/// </summary>
/// <remarks>
/// Calls <see cref="MemoryPackSerializer"/>/<see cref="JsonSerializer"/> directly rather
/// than through <see cref="ApiSerialization"/> itself - that type's job is HTTP content
/// negotiation (reading <c>Accept</c>/<c>Content-Type</c> headers), which isn't part of
/// either codec's actual serialization cost. "One" uses a single <see cref="LogEventDto"/>
/// (a hypothetical narrow response); "Page" uses a full
/// <see cref="Query.LogSearchQueryBuilder.DefaultPageSize"/> (200) page - the genuine
/// "batch" shape for this boundary, since a real <see cref="LogSearchResponse"/> does
/// carry many rows in one serialized payload (unlike the Redis-buffer side - see
/// <see cref="RedisBufferSerializationBenchmarks"/>'s remarks).
/// </remarks>
[MemoryDiagnoser]
public class ApiResponseSerializationBenchmarks
{
    private const int PageSize = 200;

    private LogEventDto _one = null!;
    private LogSearchResponse _page = null!;

    private byte[] _oneMemoryPack = null!;
    private byte[] _oneJson = null!;
    private byte[] _pageMemoryPack = null!;
    private byte[] _pageJson = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _one = LogEventDtoFixtures.One(random);
        _page = LogEventDtoFixtures.Page(random, PageSize);

        _oneMemoryPack = MemoryPackSerializer.Serialize(_one);
        _oneJson = JsonSerializer.SerializeToUtf8Bytes(_one, LogsJsonContext.Default.LogEventDto);
        _pageMemoryPack = MemoryPackSerializer.Serialize(_page);
        _pageJson = JsonSerializer.SerializeToUtf8Bytes(_page, LogsJsonContext.Default.LogSearchResponse);
    }

    [Benchmark(Baseline = true)]
    public byte[] MemoryPack_Encode_One() => MemoryPackSerializer.Serialize(_one);

    [Benchmark]
    public byte[] Json_Encode_One() => JsonSerializer.SerializeToUtf8Bytes(_one, LogsJsonContext.Default.LogEventDto);

    [Benchmark]
    public LogEventDto MemoryPack_Decode_One() => MemoryPackSerializer.Deserialize<LogEventDto>(_oneMemoryPack)!;

    [Benchmark]
    public LogEventDto? Json_Decode_One() => JsonSerializer.Deserialize(_oneJson, LogsJsonContext.Default.LogEventDto);

    [Benchmark]
    public byte[] MemoryPack_Encode_Page() => MemoryPackSerializer.Serialize(_page);

    [Benchmark]
    public byte[] Json_Encode_Page() => JsonSerializer.SerializeToUtf8Bytes(_page, LogsJsonContext.Default.LogSearchResponse);

    [Benchmark]
    public LogSearchResponse MemoryPack_Decode_Page() => MemoryPackSerializer.Deserialize<LogSearchResponse>(_pageMemoryPack)!;

    [Benchmark]
    public LogSearchResponse? Json_Decode_Page() => JsonSerializer.Deserialize(_pageJson, LogsJsonContext.Default.LogSearchResponse);
}
