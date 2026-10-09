using Flare.Api.Model;
using Flare.Api.Status;
using Xunit;

namespace Flare.Api.Tests.Status;

public class StatusIncidentsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static StatusIncident Incident(StatusIncidentStatus status, DateTimeOffset created, DateTimeOffset? last = null) => new()
    {
        Id = Guid.NewGuid(), PageId = Guid.NewGuid(), Title = "t", CreatedAt = created, UpdatedAt = last ?? created,
        Updates = [new StatusIncidentUpdate(created, StatusIncidentStatus.Investigating, "a"), new StatusIncidentUpdate(last ?? created, status, "b")],
    };

    [Fact]
    public void Open_DefaultsToInvestigating_AndTrims()
    {
        var incident = StatusIncidents.Open(Guid.NewGuid(), new StatusIncidentRequest { Title = " Down ", Message = " looking " }, Now);
        Assert.Equal("Down", incident.Title);
        Assert.Equal(StatusIncidentStatus.Investigating, incident.Status);
        Assert.Equal("looking", incident.Updates[0].Message);
        Assert.Null(incident.ResolvedAt);
    }

    [Fact]
    public void AddUpdate_AppendsAndResolves()
    {
        var open = StatusIncidents.Open(Guid.NewGuid(), new StatusIncidentRequest { Title = "x", Message = "m" }, Now);
        var (updated, error) = StatusIncidents.AddUpdate(open, new StatusIncidentUpdateRequest { Status = StatusIncidentStatus.Resolved, Message = "fixed" }, Now.AddHours(1));
        Assert.Null(error);
        Assert.Equal(2, updated!.Updates.Count);
        Assert.Equal(Now.AddHours(1), updated.ResolvedAt);
    }

    [Fact]
    public void AddUpdate_RefusesPastTheCap()
    {
        var full = Incident(StatusIncidentStatus.Monitoring, Now) with
        {
            Updates = Enumerable.Repeat(new StatusIncidentUpdate(Now, StatusIncidentStatus.Monitoring, "m"), StatusIncidents.MaxUpdates).ToList(),
        };
        var (updated, error) = StatusIncidents.AddUpdate(full, new StatusIncidentUpdateRequest { Status = StatusIncidentStatus.Monitoring, Message = "m" }, Now);
        Assert.Null(updated);
        Assert.NotNull(error);
    }

    [Fact]
    public void ForPublic_ShowsOpenFirst_ThenRecentlyResolved_AndDropsOld()
    {
        var open = Incident(StatusIncidentStatus.Identified, Now.AddHours(-2));
        var recent = Incident(StatusIncidentStatus.Resolved, Now.AddDays(-3), Now.AddDays(-2));
        var old = Incident(StatusIncidentStatus.Resolved, Now.AddDays(-30), Now.AddDays(-29));

        var shown = StatusIncidents.ForPublic([old, recent, open], Now);

        Assert.Equal(2, shown.Count);
        Assert.Null(shown[0].ResolvedAt);
        Assert.NotNull(shown[1].ResolvedAt);
        Assert.Equal("b", shown[0].Updates[0].Message); // newest update first
    }

    [Theory]
    [InlineData("", "m", false)]
    [InlineData("t", " ", false)]
    [InlineData("t", "m", true)]
    public void Request_Validates(string title, string message, bool valid) =>
        Assert.Equal(valid, new StatusIncidentRequest { Title = title, Message = message }.Validate() is null);
}
