using Flare.Ingest.Pipeline;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline;

public class ClickHouseInsertColumnTypesTests
{
    private static readonly Dictionary<string, string> Types = new()
    {
        ["Timestamp"] = "DateTime64(9)",
        ["Events.Name"] = "Array(String)",
    };

    [Fact]
    public void MissingColumn_IsNull_WhenEveryColumnHasAType() =>
        Assert.Null(ClickHouseInsertColumnTypes.MissingColumn(Types, ["Timestamp", "Events.Name"]));

    [Fact]
    public void MissingColumn_NamesTheFirstColumnWithoutAType() =>
        Assert.Equal("Body", ClickHouseInsertColumnTypes.MissingColumn(Types, ["Timestamp", "Body", "Other"]));

    [Fact]
    public void MissingColumn_IsCaseSensitive_LikeClickHouseColumnNames() =>
        Assert.Equal("timestamp", ClickHouseInsertColumnTypes.MissingColumn(Types, ["timestamp"]));
}
