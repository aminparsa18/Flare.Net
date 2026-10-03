using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

public class DashboardTagsTests
{
    [Fact]
    public void Normalize_TrimsLowercasesAndDropsBlanksAndDuplicates()
    {
        Assert.Equal(["prod", "payments"], DashboardTags.Normalize([" Prod ", "payments", "PROD", "  "]));
    }

    [Fact]
    public void Normalize_Null_IsEmpty() => Assert.Empty(DashboardTags.Normalize(null));

    [Fact]
    public void Validate_RejectsTooManyTags()
    {
        var tags = Enumerable.Range(0, DashboardTags.MaxTags + 1).Select(i => $"t{i}").ToList();

        Assert.NotNull(DashboardTags.Validate(tags));
        Assert.Null(DashboardTags.Validate(tags.Take(DashboardTags.MaxTags).ToList()));
    }

    [Fact]
    public void Validate_RejectsOverlongTag() =>
        Assert.NotNull(DashboardTags.Validate([new string('a', DashboardTags.MaxTagLength + 1)]));

    [Fact]
    public void Validate_NullAndEmpty_AreValid()
    {
        Assert.Null(DashboardTags.Validate(null));
        Assert.Null(DashboardTags.Validate([]));
    }
}
