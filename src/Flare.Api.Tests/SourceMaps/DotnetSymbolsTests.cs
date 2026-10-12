using System.IO.Compression;
using System.Runtime.CompilerServices;
using Flare.Api.SourceMaps;
using Flare.Identity.SourceMaps;
using Flare.Mcp.DotnetSymbols;
using Xunit;

namespace Flare.Api.Tests.SourceMaps;

public class DotnetSymbolsTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Marker(int alpha, string beta) => alpha + beta.Length;

    private static (DotnetSymbols Symbols, Guid Mvid) BuildForThisAssembly()
    {
        var (json, mvid, error) = DotnetSymbolsBuilder.Build(typeof(DotnetSymbolsTests).Assembly.Location);
        Assert.True(json is not null, error);
        var symbols = DotnetSymbols.Parse(json);
        Assert.Equal(mvid, symbols.Mvid);
        return (symbols, mvid);
    }

    [Fact]
    public void Builds_from_a_real_pdb_and_resolves_a_method()
    {
        var (symbols, _) = BuildForThisAssembly();

        var hit = symbols.Lookup("Flare.Api.Tests.SourceMaps.DotnetSymbolsTests.Marker", ["alpha", "beta"], 0);

        Assert.NotNull(hit);
        Assert.EndsWith("DotnetSymbolsTests.cs", hit.Value.File);
        Assert.Equal(13, hit.Value.Line);
    }

    [Fact]
    public void Unknown_method_or_ambiguous_parameters_do_not_resolve()
    {
        var (symbols, _) = BuildForThisAssembly();

        Assert.Null(symbols.Lookup("Flare.Api.Tests.SourceMaps.DotnetSymbolsTests.Nope", [], 0));
        Assert.Null(symbols.Lookup("Flare.Api.Tests.SourceMaps.DotnetSymbolsTests.Marker", ["only-one"], 0));
    }

    [Fact]
    public void Parses_mono_frames_ignoring_generic_arguments_and_aot_ids()
    {
        var line = "  at Ns.Outer`1[T].Inner.Run[U] (System.Collections.Generic.Dictionary`2[K,V] map, System.String name) [0x0001a] in <8e3f2a1b4c5d6e7f8091a2b3c4d5e6f7#2>:0";

        Assert.True(MonoStackTrace.TryParseFrame(line, out var indent, out var frame));

        Assert.Equal("  ", indent);
        Assert.Equal("Ns.Outer`1.Inner.Run", frame.Method);
        Assert.Equal(["map", "name"], frame.ParameterNames);
        Assert.Equal(0x1a, frame.IlOffset);
        Assert.Equal(Guid.Parse("8e3f2a1b4c5d6e7f8091a2b3c4d5e6f7"), frame.Mvid);
    }

    [Fact]
    public void Lines_that_are_not_mono_frames_are_not_parsed()
    {
        Assert.False(MonoStackTrace.TryParseFrame("   at Foo.Bar() in /src/Foo.cs:line 5", out _, out _));
        Assert.False(MonoStackTrace.TryParseFrame("System.InvalidOperationException: boom", out _, out _));
    }

    [Fact]
    public async Task Symbolicator_rewrites_mono_frames_from_an_uploaded_file()
    {
        var (json, mvid, _) = DotnetSymbolsBuilder.Build(typeof(DotnetSymbolsTests).Assembly.Location);
        var store = new MonoStore { Bundle = DotnetSymbols.BundleName(mvid), Content = Gzip(json!) };
        using var symbolicator = new StackTraceSymbolicator(store);
        var trace = $"System.Exception: boom\n  at Flare.Api.Tests.SourceMaps.DotnetSymbolsTests.Marker (System.Int32 alpha, System.String beta) [0x00000] in <{mvid:N}>:0\n  at Other.Thing () [0x00001] in <{Guid.Empty:N}>:0";

        var (result, symbolicated) = await symbolicator.SymbolicateAsync("app", ["1.0"], trace, CancellationToken.None);

        Assert.True(symbolicated);
        var lines = result.Split('\n');
        Assert.Equal("System.Exception: boom", lines[0]);
        Assert.Matches(@"^  at Flare\.Api\.Tests\.SourceMaps\.DotnetSymbolsTests\.Marker\(System\.Int32 alpha, System\.String beta\) in .*DotnetSymbolsTests\.cs:line 13$", lines[1]);
        Assert.Contains("[0x00001]", lines[2]);
    }

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    private sealed class MonoStore : ISourceMapStore
    {
        public required string Bundle { get; init; }
        public required byte[] Content { get; init; }

        public Task<(byte[] GzipContent, DateTimeOffset UploadedAt)?> GetAsync(string serviceName, string version, string bundle, CancellationToken cancellationToken = default) =>
            Task.FromResult<(byte[], DateTimeOffset)?>(serviceName == "app" && version == "1.0" && bundle == Bundle ? (Content, DateTimeOffset.UnixEpoch) : null);

        public Task<IReadOnlyList<string>> ListBundlesAsync(string serviceName, string version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertAsync(string serviceName, string version, string bundle, byte[] gzipContent, long sizeBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SourceMapInfo>> ListAsync(string? serviceName, string? version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> DeleteAsync(string serviceName, string version, string? bundle, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
