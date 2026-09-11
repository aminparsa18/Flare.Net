using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="INotificationChannelQueryService"/> - lets
/// <c>NotificationChannelResolver</c>'s <c>ResolveAsync</c> be unit-tested with no
/// ClickHouse involved, same "pure logic, fake the one interface it depends on"
/// convention as <see cref="FakeUserStore"/>.</summary>
internal sealed class FakeNotificationChannelQueryService : INotificationChannelQueryService
{
    private readonly Dictionary<Guid, NotificationChannel> _channelsById = [];

    public void Seed(NotificationChannel channel) => _channelsById[channel.Id] = channel;

    public Task<NotificationChannel> CreateAsync(NotificationChannelRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not needed by NotificationChannelResolver tests.");

    public Task<IReadOnlyList<NotificationChannel>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationChannel>>(_channelsById.Values.ToList());

    public Task<NotificationChannel?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_channelsById.GetValueOrDefault(id));

    public Task<IReadOnlyList<NotificationChannel>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationChannel>>(ids.Where(_channelsById.ContainsKey).Select(id => _channelsById[id]).ToList());

    public Task<NotificationChannel?> UpdateAsync(Guid id, NotificationChannelRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not needed by NotificationChannelResolver tests.");

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not needed by NotificationChannelResolver tests.");
}
