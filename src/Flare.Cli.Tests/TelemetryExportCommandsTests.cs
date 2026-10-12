using Flare.Cli.Commands;
using Xunit;

namespace Flare.Cli.Tests;

public class TelemetryExportCommandsTests
{
    [Fact]
    public void ParseSignals_NormalizesCaseAndDeduplicates()
    {
        var signals = TelemetryExportClient.ParseSignals(["LOGS", "traces", "logs"]);

        Assert.Equal(["Logs", "Traces"], signals);
    }

    [Fact]
    public void ParseSignals_RejectsUnknownSignal() =>
        Assert.Null(TelemetryExportClient.ParseSignals(["logs", "profiles"]));

    [Fact]
    public void ParseHeaders_SplitsOnFirstEqualsSoValuesMayContainOne()
    {
        var headers = TelemetryExportClient.ParseHeaders(["Authorization=Basic abc==", "X-Org=acme"]);

        Assert.NotNull(headers);
        Assert.Equal("Basic abc==", headers["authorization"]);
        Assert.Equal("acme", headers["X-Org"]);
    }

    [Theory]
    [InlineData("NoEquals")]
    [InlineData("=value")]
    public void ParseHeaders_RejectsMalformedHeader(string header) =>
        Assert.Null(TelemetryExportClient.ParseHeaders([header]));
}
