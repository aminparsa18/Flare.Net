using System.IO.Compression;
using System.Text;
using Flare.Api.SourceMaps;
using Flare.Identity.SourceMaps;
using Xunit;

namespace Flare.Api.Tests.SourceMaps;

public class StackTraceSymbolicatorTests
{
    private const string Map = """{"version":3,"sources":["src/app.ts"],"names":["handleClick"],"mappings":"AAAAA"}""";

    private sealed class FakeStore : ISourceMapStore
    {
        public Dictionary<(string, string, string), byte[]> Maps { get; } = [];

        public Task<IReadOnlyList<string>> ListBundlesAsync(string serviceName, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([.. Maps.Keys.Where(k => k.Item1 == serviceName && k.Item2 == version).Select(k => k.Item3)]);

        public Task<(byte[] GzipContent, DateTimeOffset UploadedAt)?> GetAsync(string serviceName, string version, string bundle, CancellationToken cancellationToken = default) =>
            Task.FromResult<(byte[], DateTimeOffset)?>(Maps.TryGetValue((serviceName, version, bundle), out var bytes) ? (bytes, DateTimeOffset.UnixEpoch) : null);

        public Task UpsertAsync(string serviceName, string version, string bundle, byte[] gzipContent, long sizeBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<SourceMapInfo>> ListAsync(string? serviceName, string? version, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> DeleteAsync(string serviceName, string version, string? bundle, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    [Fact]
    public async Task SymbolicateAsync_MapsFramesOfAMatchingBundle()
    {
        var store = new FakeStore();
        store.Maps[("web", "1.2.0", "assets/index-abc.js")] = Gzip(Map);
        using var symbolicator = new StackTraceSymbolicator(store);

        var (text, ok) = await symbolicator.SymbolicateAsync("web", ["", "1.2.0"],
            "TypeError: boom\n    at t (https://app.example.com/assets/index-abc.js:1:1)", CancellationToken.None);

        Assert.True(ok);
        Assert.Equal("TypeError: boom\n    at t (src/app.ts:1:1)", text);
    }

    [Fact]
    public async Task SymbolicateAsync_WithNoMapForTheVersion_LeavesTheTraceAlone()
    {
        var store = new FakeStore();
        store.Maps[("web", "1.2.0", "assets/index-abc.js")] = Gzip(Map);
        using var symbolicator = new StackTraceSymbolicator(store);
        const string trace = "    at t (https://app.example.com/assets/index-abc.js:1:1)";

        Assert.Equal((trace, false), await symbolicator.SymbolicateAsync("web", ["9.9.9"], trace, CancellationToken.None));
        Assert.Equal((trace, false), await symbolicator.SymbolicateAsync("other", ["1.2.0"], trace, CancellationToken.None));
    }

    [Fact]
    public async Task SymbolicateAsync_WithACorruptStoredMap_LeavesTheTraceAlone()
    {
        var store = new FakeStore();
        store.Maps[("web", "1", "app.js")] = Gzip("not a map");
        using var symbolicator = new StackTraceSymbolicator(store);
        const string trace = "    at t (https://x.test/app.js:1:1)";

        Assert.Equal((trace, false), await symbolicator.SymbolicateAsync("web", ["1"], trace, CancellationToken.None));
    }

    [Theory]
    [InlineData("/assets/index-abc.js", "assets/index-abc.js")]
    [InlineData("/static/assets/index-abc.js", "assets/index-abc.js")]
    [InlineData("/other/index-abc.js", "index-abc.js")]
    [InlineData("/xindex-abc.js", null)]
    public void BestBundle_PicksTheLongestPathSuffix(string urlPath, string? expected) =>
        Assert.Equal(expected, StackTraceSymbolicator.BestBundle(["index-abc.js", "assets/index-abc.js", "other.js"], urlPath));
}
