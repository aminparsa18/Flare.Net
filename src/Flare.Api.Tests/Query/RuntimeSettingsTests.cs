using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class RuntimeSettingsTests
{
    [Fact]
    public void Request_requires_enabled()
    {
        Assert.NotNull(new PiiRedactionSettingRequest().Validate());
        Assert.Null(new PiiRedactionSettingRequest { Enabled = false }.Validate());
        Assert.Null(new PiiRedactionSettingRequest { Enabled = true }.Validate());
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", null)]
    [InlineData("\"yes\"", null)]
    [InlineData("not json", null)]
    public void Stored_value_reads_only_json_booleans(string json, bool? expected) =>
        Assert.Equal(expected, RuntimeSettingsQueryService.ParseBool(json));
}
