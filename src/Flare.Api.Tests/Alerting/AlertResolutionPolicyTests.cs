using Flare.Api.Alerting;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="AlertResolutionPolicy"/> - the pure firing→ok decision behind "Resolved"
/// notifications. Reading the firing state from ClickHouse and sending/recording the
/// resolution live in <c>AlertEvaluationWorker</c> and are covered by end-to-end runs, same
/// split as the rest of this project.
/// </summary>
public class AlertResolutionPolicyTests
{
    private static readonly DateTimeOffset FiredAt = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NotFiring_DoesNothing(bool windowActive)
    {
        Assert.Equal(AlertResolutionAction.None, AlertResolutionPolicy.Decide(null, windowActive));
    }

    [Fact]
    public void NotifiedFire_NoWindow_Notifies()
    {
        Assert.Equal(AlertResolutionAction.Notify, AlertResolutionPolicy.Decide(new AlertFiringState(FiredAt, Notified: true), maintenanceWindowActive: false));
    }

    [Fact]
    public void NotifiedFire_DuringWindow_DefersUntilItEnds()
    {
        Assert.Equal(AlertResolutionAction.Defer, AlertResolutionPolicy.Decide(new AlertFiringState(FiredAt, Notified: true), maintenanceWindowActive: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlySuppressedFires_RecordsWithoutSending(bool windowActive)
    {
        Assert.Equal(AlertResolutionAction.RecordOnly, AlertResolutionPolicy.Decide(new AlertFiringState(FiredAt, Notified: false), windowActive));
    }
}
