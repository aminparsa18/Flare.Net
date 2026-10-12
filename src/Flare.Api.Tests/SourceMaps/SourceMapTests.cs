using System.Text;
using Flare.Api.SourceMaps;
using Xunit;

namespace Flare.Api.Tests.SourceMaps;

public class SourceMapTests
{
    private static SourceMap Parse(string json) => SourceMap.Parse(Encoding.UTF8.GetBytes(json));

    private const string Map = """{"version":3,"sources":["src/app.ts"],"names":["foo","bar"],"mappings":"AAAAA,IAAIC;AACA"}""";

    [Fact]
    public void Lookup_ReturnsTheSegmentAtOrBeforeTheColumn()
    {
        var map = Parse(Map);

        Assert.Equal(new OriginalPosition("src/app.ts", 0, 0, "foo"), map.Lookup(0, 0));
        Assert.Equal(new OriginalPosition("src/app.ts", 0, 0, "foo"), map.Lookup(0, 3));
        Assert.Equal(new OriginalPosition("src/app.ts", 0, 4, "bar"), map.Lookup(0, 4));
        Assert.Equal(new OriginalPosition("src/app.ts", 0, 4, "bar"), map.Lookup(0, 99));
    }

    [Fact]
    public void Lookup_DecodesRelativeLinesAndCarriesTheSourceColumnAcrossLines()
    {
        Assert.Equal(new OriginalPosition("src/app.ts", 1, 4, null), Parse(Map).Lookup(1, 7));
    }

    [Fact]
    public void Lookup_OutsideTheMappedRange_ReturnsNull()
    {
        var map = Parse(Map);

        Assert.Null(map.Lookup(5, 0));
        Assert.Null(map.Lookup(-1, 0));
    }

    [Fact]
    public void Lookup_DecodesNegativeAndMultiDigitValues()
    {
        // "gBAAgB": genCol +16, src 0, line 0, col +16; then "DAAD": genCol -1 is invalid ordering handled by sort, col -1.
        var map = Parse("""{"version":3,"sources":["a.ts"],"names":[],"mappings":"gBAAgB,DAAD"}""");

        Assert.Equal(new OriginalPosition("a.ts", 0, 16, null), map.Lookup(0, 16));
        Assert.Equal(new OriginalPosition("a.ts", 0, 15, null), map.Lookup(0, 15));
    }

    [Fact]
    public void Parse_PrefixesSourceRoot()
    {
        var map = Parse("""{"version":3,"sourceRoot":"webpack:///","sources":["./src/a.ts"],"names":[],"mappings":"AAAA"}""");

        Assert.Equal("webpack:///./src/a.ts", map.Lookup(0, 0)!.Value.Source);
    }

    [Theory]
    [InlineData("""{"version":3,"sections":[],"mappings":""}""")]
    [InlineData("""{"version":2,"sources":[],"names":[],"mappings":""}""")]
    [InlineData("""{"version":3,"sources":[],"names":[]}""")]
    [InlineData("""{"version":3,"sources":["a"],"names":[],"mappings":"AAAA,@"}""")]
    [InlineData("""{"version":3,"sources":["a"],"names":[],"mappings":"ACAA"}""")]
    [InlineData("not json")]
    public void Parse_RejectsUnusableMaps(string json) => Assert.Throws<FormatException>(() => Parse(json));
}
