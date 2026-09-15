using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IDashboardQueryService"/> - lets <see cref="Endpoints.DashboardEndpoints"/>'
/// handlers be unit-tested with no ClickHouse involved, same "pure logic, fake the one
/// interface it depends on" convention as <see cref="FakeNotificationChannelQueryService"/>.
/// Doesn't replicate <c>DashboardQueryService</c>'s tombstone-versioning mechanics - a plain
/// dictionary is enough for the ownership-check logic these tests exercise (see ADR-0027);
/// the real ClickHouse CRUD stays covered by e2e runs per this repo's usual convention for
/// ClickHouse-touching services.
/// </summary>
internal sealed class FakeDashboardQueryService : IDashboardQueryService
{
    private readonly Dictionary<Guid, Dashboard> _dashboardsById = [];

    public void Seed(Dashboard dashboard) => _dashboardsById[dashboard.Id] = dashboard;

    public Task<Dashboard> CreateAsync(DashboardRequest request, Guid? ownerUserId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dashboard = new Dashboard
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description ?? "",
            OwnerUserId = ownerUserId,
            LayoutJson = request.LayoutJson,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _dashboardsById[dashboard.Id] = dashboard;
        return Task.FromResult(dashboard);
    }

    public Task<IReadOnlyList<Dashboard>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Dashboard>>(_dashboardsById.Values.ToList());

    public Task<Dashboard?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_dashboardsById.GetValueOrDefault(id));

    public Task<Dashboard?> UpdateAsync(Guid id, DashboardRequest request, CancellationToken cancellationToken)
    {
        if (!_dashboardsById.TryGetValue(id, out var existing))
        {
            return Task.FromResult<Dashboard?>(null);
        }

        var updated = existing with
        {
            Name = request.Name,
            Description = request.Description ?? "",
            LayoutJson = request.LayoutJson,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _dashboardsById[id] = updated;
        return Task.FromResult<Dashboard?>(updated);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_dashboardsById.Remove(id));
}
