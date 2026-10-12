using System.IO.Compression;
using System.Text;
using Flare.Api.SourceMaps;
using Flare.Identity.SourceMaps;
using Flare.Mcp.DotnetSymbols;
using Xunit;

namespace Flare.Api.Tests.SourceMaps;

public class NativeSymbolsTests
{
    // A clang-built Mach-O DWARF file (DWARF 4) with functions named the way ILC mangles them:
    // Cart__Checkout (lines 3-5 at offsets 7/19/34), Cart__Add (line 8), and the overloads Cart__Add/Cart__Add_0.
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "SourceMaps", "Fixtures", "aot-fixture.dwarf");

    private static NativeSymbols Load()
    {
        var (json, uuid, error) = NativeSymbolsBuilder.Build(FixturePath);
        Assert.True(json is not null, error);
        var symbols = NativeSymbols.Parse(json);
        Assert.Equal(uuid, symbols.Uuid);
        return symbols;
    }

    [Fact]
    public void Resolves_a_function_offset_to_its_source_line()
    {
        var symbols = Load();

        // Offsets are return addresses, so the instruction is the byte before: 0x19 -> the row starting at 0x13 (line 4).
        Assert.Equal(("Cart.cs", 4), Trim(symbols.Lookup("My.Shop.Cart.Checkout", 0x1a)));
        Assert.Equal(("Cart.cs", 3), Trim(symbols.Lookup("My.Shop.Cart.Checkout", 0x8)));
    }

    [Fact]
    public void Overloads_are_left_unresolved()
    {
        var symbols = Load();

        Assert.Null(symbols.Lookup("My.Shop.Cart.Add", 0x8));
    }

    [Fact]
    public void Unknown_functions_do_not_resolve()
    {
        Assert.Null(Load().Lookup("My.Shop.Cart.Missing", 4));
    }

    [Theory]
    [InlineData("My.Shop.Cart.Add", "My_Shop_Cart__Add")]
    [InlineData("My.Shop.Cart.Nested.Deep", "My_Shop_Cart_Nested__Deep")]
    [InlineData("My.Shop.Cart.<>c__DisplayClass3_0.<Inst>b__0", "My_Shop_Cart___c__DisplayClass3_0___Inst_b__0")]
    [InlineData("My.Shop.Cart.Gen[T]", "My_Shop_Cart__Gen")]
    public void Frame_names_are_mangled_like_linkage_names(string frame, string expected) =>
        Assert.Equal(expected, NativeSymbols.Mangle(frame));

    [Fact]
    public void Parses_native_aot_frames()
    {
        Assert.True(NativeAotStackTrace.TryParseFrame("   at My.Shop.Cart.Gen[T](T) + 0x11", out var indent, out var frame));

        Assert.Equal("   ", indent);
        Assert.Equal("My.Shop.Cart.Gen[T]", frame.Method);
        Assert.Equal(0x11, frame.Offset);
        Assert.False(NativeAotStackTrace.TryParseFrame("   at Foo.Bar() in /src/Foo.cs:line 5", out _, out _));
    }

    [Fact]
    public async Task Symbolicator_rewrites_native_frames_from_an_uploaded_file()
    {
        var (json, uuid, _) = NativeSymbolsBuilder.Build(FixturePath);
        var store = new NativeStore { Bundle = NativeSymbols.BundleName(uuid!), Content = Gzip(json!) };
        using var symbolicator = new StackTraceSymbolicator(store);
        var trace = "System.Exception: boom\n   at My.Shop.Cart.Checkout(Int32) + 0x1a\n   at Other.Thing() + 0x10";

        var (result, symbolicated) = await symbolicator.SymbolicateAsync("app", ["1.0"], trace, CancellationToken.None);

        Assert.True(symbolicated);
        var lines = result.Split('\n');
        Assert.Matches(@"^   at My\.Shop\.Cart\.Checkout\(Int32\) in .*Cart\.cs:line 4$", lines[1]);
        Assert.Equal("   at Other.Thing() + 0x10", lines[2]);
    }

    private static (string File, int Line)? Trim((string File, int Line)? hit) =>
        hit is { } h ? (Path.GetFileName(h.File), h.Line) : null;

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    private sealed class NativeStore : ISourceMapStore
    {
        public required string Bundle { get; init; }
        public required byte[] Content { get; init; }

        public Task<IReadOnlyList<string>> ListBundlesAsync(string serviceName, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(serviceName == "app" && version == "1.0" ? [Bundle] : []);

        public Task<(byte[] GzipContent, DateTimeOffset UploadedAt)?> GetAsync(string serviceName, string version, string bundle, CancellationToken cancellationToken = default) =>
            Task.FromResult<(byte[], DateTimeOffset)?>(bundle == Bundle ? (Content, DateTimeOffset.UnixEpoch) : null);

        public Task UpsertAsync(string serviceName, string version, string bundle, byte[] gzipContent, long sizeBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SourceMapInfo>> ListAsync(string? serviceName, string? version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> DeleteAsync(string serviceName, string version, string? bundle, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
