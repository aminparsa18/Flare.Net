using System.IO.Compression;
using Flare.Api.SourceMaps;
using Flare.Identity.SourceMaps;
using Flare.Mcp.DotnetSymbols;
using Xunit;

namespace Flare.Api.Tests.SourceMaps;

/// <summary>Native AOT overloads (<c>Add</c>, <c>Add_0</c>, ...) told apart by parameter types, ADR-0171.</summary>
public class NativeOverloadTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "SourceMaps", "Fixtures", "aot-fixture.dwarf");

    // The fixture's functions: Cart__Add covers Cart.cs lines 7-9, Cart__Add_0 lines 10-12.
    private static ManagedMethod Method(string symbol, string baseName, string? parameters, int first, int last, string document = "Cart.cs") =>
        new(symbol, baseName, parameters, [new ManagedMethod.Span(document, first, last)]);

    [Fact]
    public void Repeated_names_get_the_first_free_numeric_suffix_in_metadata_order()
    {
        Assert.Equal(["Add", "Add_0", "Remove", "Add_1"], ManagedOverloads.AssignNames(["Add", "Add", "Remove", "Add"]));
    }

    [Fact]
    public void A_method_really_named_with_a_suffix_takes_part_in_the_numbering()
    {
        // Add, Add -> Add, Add_0; the real Add_0 is then taken and becomes Add_0_0.
        Assert.Equal(["Add", "Add_0", "Add_0_0"], ManagedOverloads.AssignNames(["Add", "Add", "Add_0"]));
    }

    [Fact]
    public void Names_are_sanitized_before_they_are_compared()
    {
        Assert.Equal(["_ctor", "_ctor_0", "_cctor", "_Main_b__0_0"], ManagedOverloads.AssignNames([".ctor", ".ctor", ".cctor", "<Main>b__0_0"]));
    }

    [Fact]
    public void Reads_the_parameter_types_the_way_a_native_frame_prints_them()
    {
        var (methods, error) = ManagedOverloads.Read(typeof(NativeOverloadTests).Assembly.Location);
        Assert.True(methods is not null, error);

        string? Parameters(string symbolSuffix) => Assert.Single(methods, m => m.Symbol.EndsWith(symbolSuffix, StringComparison.Ordinal)).Parameters;

        Assert.Equal("String", Parameters("_Sample__Add"));
        Assert.Equal("Int32", Parameters("_Sample__Add_0"));
        Assert.Equal("List`1", Parameters("_Sample__Add_1"));
        Assert.Equal("Int32[]", Parameters("_Sample__Add_2"));
        Assert.Equal("Int32[,]", Parameters("_Sample__Add_3"));
        Assert.Equal("Int32&", Parameters("_Sample__Add_4"));
        Assert.Equal("String, Int32", Parameters("_Sample__Add_5"));
        Assert.Equal("T", Parameters("_Sample__Add_6"));
        Assert.Equal("NativeOverloadTests.Sample.Nested", Parameters("_Sample__Add_7"));
        Assert.Equal("Nullable`1, Double[][]", Parameters("_Sample__Add_8"));
        Assert.Equal("", Parameters("_Sample__Add_9"));
        Assert.Equal("T", Parameters("_Box_1__Put"));
        Assert.Equal("List`1", Parameters("_Box_1__Put_0"));
        Assert.Equal("U, T", Parameters("_Box_1__Put_1"));
    }

    [Fact]
    public void Records_the_frame_form_name_without_the_overload_number()
    {
        var (methods, _) = ManagedOverloads.Read(typeof(NativeOverloadTests).Assembly.Location);

        var second = Assert.Single(methods!, m => m.Symbol.EndsWith("_Sample__Add_0", StringComparison.Ordinal));
        Assert.Equal(NativeSymbols.Mangle("Flare.Api.Tests.SourceMaps.NativeOverloadTests.Sample.Add"), second.Base);
        Assert.StartsWith("Flare_Api_Tests_", second.Symbol);
        Assert.Contains(second.Spans, s => s.Document == "NativeOverloadTests.cs");
    }

    [Fact]
    public void Overloads_resolve_by_the_parameter_types_the_frame_prints()
    {
        var symbols = Build(Method("aot_My_Shop_Cart__Add", "My_Shop_Cart__Add", "String", 7, 9), Method("aot_My_Shop_Cart__Add_0", "My_Shop_Cart__Add", "Int32, String", 10, 12));

        Assert.Equal(("Cart.cs", 8), Trim(symbols.Lookup("My.Shop.Cart.Add", "String", 0x8)));
        Assert.Equal(("Cart.cs", 11), Trim(symbols.Lookup("My.Shop.Cart.Add", "Int32, String", 0x8)));
        Assert.Equal(("Cart.cs", 11), Trim(symbols.Lookup("My.Shop.Cart.Add", "Int32,String", 0x8)));
    }

    [Fact]
    public void A_frame_whose_types_match_no_known_overload_stays_unresolved()
    {
        var symbols = Build(Method("aot_My_Shop_Cart__Add", "My_Shop_Cart__Add", "String", 7, 9), Method("aot_My_Shop_Cart__Add_0", "My_Shop_Cart__Add", "Int32", 10, 12));

        Assert.Null(symbols.Lookup("My.Shop.Cart.Add", "Double", 0x8));
        Assert.Null(symbols.Lookup("My.Shop.Cart.Add", null, 0x8));
    }

    [Fact]
    public void Overloads_that_print_alike_stay_unresolved_when_they_disagree()
    {
        // Two types called Item in different namespaces print the same parameter list.
        var symbols = Build(Method("aot_My_Shop_Cart__Add", "My_Shop_Cart__Add", "Item", 7, 9), Method("aot_My_Shop_Cart__Add_0", "My_Shop_Cart__Add", "Item", 10, 12));

        Assert.Null(symbols.Lookup("My.Shop.Cart.Add", "Item", 0x8));
    }

    [Fact]
    public void Generic_instantiations_of_one_overload_resolve_together()
    {
        // Foo(T) is shared (__Canon) code, Foo(Int32) exists once per instantiation of the declaring type.
        var symbols = Synthetic(
            ("_aot_My_Shop_Gen_1<System___Canon>__Foo", 8, "My_Shop_Gen_1__Foo", "T"),
            ("_aot_My_Shop_Gen_1<Int32>__Foo_0", 11, "My_Shop_Gen_1__Foo", "Int32"),
            ("_aot_My_Shop_Gen_1<Int64>__Foo_0", 11, "My_Shop_Gen_1__Foo", "Int32"),
            ("_aot_My_Shop_Gen_1<System___Canon>__Foo_1<Int64>", 14, "My_Shop_Gen_1__Foo", "U, T"));

        Assert.Equal(11, symbols.Lookup("My.Shop.Gen`1.Foo", "Int32", 0x4)?.Line);
        Assert.Equal(8, symbols.Lookup("My.Shop.Gen`1.Foo", "T", 0x4)?.Line);
        Assert.Equal(14, symbols.Lookup("My.Shop.Gen`1.Foo[U]", "U, T", 0x4)?.Line);
    }

    [Fact]
    public void Instantiation_arguments_inside_a_symbol_are_not_part_of_its_name()
    {
        Assert.Equal("aot_My_Shop_Gen_1__Foo_0", NativeSymbols.Normalize("_aot_My_Shop_Gen_1<Int32>__Foo_0"));
        Assert.Equal("aot_My_Shop_Gen_1__Foo_2", NativeSymbols.Normalize("_aot_My_Shop_Gen_1<System___Canon>__Foo_2<Int64>"));
    }

    [Fact]
    public void Functions_are_tied_to_their_overload_only_when_the_pdb_lines_agree()
    {
        var notes = new List<string>();
        var managed = new[]
        {
            Method("aot_My_Shop_Cart__Add", "My_Shop_Cart__Add", "String", 7, 9),
            // the dll of another build: this overload is somewhere else in the file
            Method("aot_My_Shop_Cart__Add_0", "My_Shop_Cart__Add", "Int32", 40, 44),
        };

        var (json, _, error) = NativeSymbolsBuilder.Build(File.ReadAllBytes(FixturePath), managed, notes.Add);

        Assert.True(json is not null, error);
        Assert.Contains("1 function(s) tied to their managed overload, 1 left out", Assert.Single(notes));
        var symbols = NativeSymbols.Parse(json);
        Assert.Equal(("Cart.cs", 8), Trim(symbols.Lookup("My.Shop.Cart.Add", "String", 0x8)));
        // Add_0 was left without a signature, so it still can't be told from Add by anything but agreement.
        Assert.Null(symbols.Lookup("My.Shop.Cart.Add", "Int32", 0x8));
    }

    [Fact]
    public void A_symbol_two_assemblies_disagree_on_is_not_used()
    {
        var notes = new List<string>();
        var managed = new[]
        {
            Method("aot_My_Shop_Cart__Add", "My_Shop_Cart__Add", "String", 7, 9),
            Method("aot_My_Shop_Cart__Add", "My_Shop_Cart__Add", "Int32", 7, 9),
        };

        NativeSymbolsBuilder.Build(File.ReadAllBytes(FixturePath), managed, notes.Add);

        Assert.StartsWith("0 function(s) tied", Assert.Single(notes));
    }

    [Fact]
    public async Task Symbolicator_resolves_overloaded_frames_from_an_uploaded_file()
    {
        var (json, uuid, error) = NativeSymbolsBuilder.Build(File.ReadAllBytes(FixturePath), [
            Method("aot_My_Shop_Cart__Add", "My_Shop_Cart__Add", "String", 7, 9),
            Method("aot_My_Shop_Cart__Add_0", "My_Shop_Cart__Add", "Int32", 10, 12)]);
        Assert.True(json is not null, error);
        using var symbolicator = new StackTraceSymbolicator(new Store { Bundle = NativeSymbols.BundleName(uuid!), Content = Gzip(json) });

        var (result, symbolicated) = await symbolicator.SymbolicateAsync("app", ["1.0"], "System.Exception: boom\n   at My.Shop.Cart.Add(Int32) + 0x8\n   at My.Shop.Cart.Add(String) + 0x8", CancellationToken.None);

        Assert.True(symbolicated);
        var lines = result.Split('\n');
        Assert.Matches(@"^   at My\.Shop\.Cart\.Add\(Int32\) in .*Cart\.cs:line 11$", lines[1]);
        Assert.Matches(@"^   at My\.Shop\.Cart\.Add\(String\) in .*Cart\.cs:line 8$", lines[2]);
    }

    private static NativeSymbols Synthetic(params (string Symbol, int Line, string? Base, string? Parameters)[] functions) =>
        NativeSymbols.Parse(NativeSymbols.Serialize(
            "00000000-0000-0000-0000-000000000001",
            ["Cart.cs"],
            functions.Select(f => (f.Symbol, new[] { (0, f.Line, 0) }, f.Base, f.Parameters))));

    private static NativeSymbols Build(params ManagedMethod[] managed)
    {
        var (json, _, error) = NativeSymbolsBuilder.Build(File.ReadAllBytes(FixturePath), managed);
        Assert.True(json is not null, error);
        return NativeSymbols.Parse(json);
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

    private sealed class Store : ISourceMapStore
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

    // Overloads whose compiler names and printed parameter lists the tests above read back from this assembly's own metadata.
    internal sealed class Sample
    {
        public sealed class Nested;

        public void Add(string s) => Throw(s);
        public void Add(int i) => Throw(i);
        public void Add(List<string> l) => Throw(l);
        public void Add(int[] a) => Throw(a);
        public void Add(int[,] a) => Throw(a);
        public void Add(ref int r) => Throw(r);
        public void Add(string s, int i) => Throw(s + i);
        public void Add<T>(T t) => Throw(t);
        public void Add(Nested n) => Throw(n);
        public void Add(int? n, double[][] d) => Throw(n + "" + d);
        public void Add() => Throw(null);

        private static void Throw(object? o) => throw new InvalidOperationException(o?.ToString());
    }

    internal sealed class Box<T>
    {
        public void Put(T t) => throw new InvalidOperationException(t?.ToString());
        public void Put(List<T> l) => throw new InvalidOperationException(l.ToString());
        public void Put<U>(U u, T t) => throw new InvalidOperationException(u + "" + t);
    }
}
