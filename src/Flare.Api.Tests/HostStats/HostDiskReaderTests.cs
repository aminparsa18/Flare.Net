using Flare.Api.HostStats;
using Xunit;

namespace Flare.Api.Tests.HostStats;

/// <summary>Tests for <see cref="HostDiskReader"/>'s disk-stat reading, using its internal path-injecting constructor to point at a scratch directory instead of depending on <c>/</c> in the test environment.</summary>
public class HostDiskReaderTests
{
    [Fact]
    public void Read_ReportsPlausibleTotalAndUsedBytes_ForAGivenPath()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var reader = new HostDiskReader(tempDir.FullName);

            var (total, used) = reader.Read();

            Assert.True(total > 0);
            Assert.True(used >= 0);
            Assert.True(used <= total);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
