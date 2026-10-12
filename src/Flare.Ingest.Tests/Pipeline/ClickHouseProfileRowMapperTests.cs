using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using Xunit;

namespace Flare.Ingest.Tests.Pipeline;

public class ClickHouseProfileRowMapperTests
{
    private static ProfileSampleRecord Sample() => new()
    {
        Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        DurationNano = 1,
        Stack = ["main", "foo"],
        Value = 9,
        ResourceAttributes = new Dictionary<string, string>(),
        SampleAttributes = new Dictionary<string, string>(),
        IngestedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero),
    };

    [Fact]
    public void ToRow_HasOneValuePerColumn_AndNullStringsBecomeEmpty()
    {
        var row = ClickHouseProfileRowMapper.ToRow(Sample());

        Assert.Equal(ClickHouseProfileRowMapper.Columns.Count, row.Length);
        Assert.Equal(string.Empty, row[ClickHouseProfileRowMapper.Columns.ToList().IndexOf("TraceId")]);
        Assert.Equal(string.Empty, row[ClickHouseProfileRowMapper.Columns.ToList().IndexOf("ServiceName")]);
    }

    [Fact]
    public void ToRow_DoesNotInsertTheMaterializedStackHash()
    {
        Assert.DoesNotContain("StackHash", ClickHouseProfileRowMapper.Columns);
    }

    [Fact]
    public void ToRow_StackIsAStringArray_InOrder()
    {
        var row = ClickHouseProfileRowMapper.ToRow(Sample());

        Assert.Equal(new[] { "main", "foo" }, Assert.IsType<string[]>(row[ClickHouseProfileRowMapper.Columns.ToList().IndexOf("Stack")]));
    }
}
