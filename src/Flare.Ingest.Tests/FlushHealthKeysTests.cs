using Flare.Ingest.Stats;
using Xunit;

namespace Flare.Ingest.Tests;

public class FlushHealthKeysTests
{
    [Theory]
    [InlineData(IngestionSignal.Logs, "flare:ingestion:flush:logs")]
    [InlineData(IngestionSignal.Traces, "flare:ingestion:flush:traces")]
    [InlineData(IngestionSignal.Metrics, "flare:ingestion:flush:metrics")]
    public void HashKey_IsLowercaseSignal(IngestionSignal signal, string expected)
    {
        Assert.Equal(expected, FlushHealthKeys.HashKey(signal));
    }

    [Fact]
    public void HashKey_DifferentSignals_ProduceDifferentKeys()
    {
        Assert.NotEqual(FlushHealthKeys.HashKey(IngestionSignal.Logs), FlushHealthKeys.HashKey(IngestionSignal.Traces));
        Assert.NotEqual(FlushHealthKeys.HashKey(IngestionSignal.Traces), FlushHealthKeys.HashKey(IngestionSignal.Metrics));
    }
}
