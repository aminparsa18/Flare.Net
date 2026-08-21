using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LogAttributeKeysQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow()
    {
        // See LogSearchQueryBuilderTests' equivalent test: System.Text.Json overwrites
        // request.Filter's default back to null when "filter" is absent from the body.
        var result = LogAttributeKeysQueryBuilder.Build(new LogAttributeKeysRequest { Filter = null! }, Now);

        Assert.Contains("WHERE Timestamp >=", result.Sql);
    }

    [Fact]
    public void Build_EnumeratesLogAttributeKeys_ViaArrayJoin()
    {
        var result = LogAttributeKeysQueryBuilder.Build(new LogAttributeKeysRequest(), Now);

        Assert.Contains("arrayJoin(mapKeys(LogAttributes)) AS Key", result.Sql);
    }

    [Fact]
    public void Build_FiltersToNumericValues_InOuterQuery()
    {
        var result = LogAttributeKeysQueryBuilder.Build(new LogAttributeKeysRequest(), Now);

        Assert.Contains("WHERE toFloat64OrNull(RawValue) IS NOT NULL", result.Sql);
        Assert.Contains("GROUP BY Key", result.Sql);
        Assert.Contains("ORDER BY NumericCount DESC", result.Sql);
    }
}
