using Flare.Identity.Audit;
using Flare.Identity.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Flare.Identity.Tests.Audit;

public class SqliteAuditEventStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityTestDatabase _database = new();
    private SqliteAuditEventStore _store = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new SqliteAuditEventStore(_database.ConnectionFactory);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    private static NewAuditEvent Event(DateTimeOffset at, string resourceType = "alert", string action = "update", Guid? actor = null, string? resourceId = "r1") =>
        new(at, actor, "admin", "session", action, resourceType, resourceId, "PUT /api/alerts/{id:guid}", 204, "10.0.0.1");

    [Fact]
    public async Task AppendAsync_ThenQueryAsync_RoundTripsEveryField()
    {
        var actor = Guid.NewGuid();
        await _store.AppendAsync(Event(T0, actor: actor));

        var row = Assert.Single(await _store.QueryAsync(new AuditEventFilter(), null, 10));

        Assert.Equal(T0, row.Timestamp);
        Assert.Equal(actor, row.ActorId);
        Assert.Equal("admin", row.ActorName);
        Assert.Equal("session", row.ActorKind);
        Assert.Equal("update", row.Action);
        Assert.Equal("alert", row.ResourceType);
        Assert.Equal("r1", row.ResourceId);
        Assert.Equal("PUT /api/alerts/{id:guid}", row.Route);
        Assert.Equal(204, row.StatusCode);
        Assert.Equal("10.0.0.1", row.SourceIp);
    }

    [Fact]
    public async Task AppendAsync_RoundTripsChanges_AndNullWhenAbsent()
    {
        const string changes = """[{"field":"threshold","before":"5","after":"10"}]""";
        await _store.AppendAsync(Event(T0) with { Changes = changes });
        await _store.AppendAsync(Event(T0.AddMinutes(1)));

        var rows = await _store.QueryAsync(new AuditEventFilter(), null, 10);

        Assert.Null(rows[0].Changes);
        Assert.Equal(changes, rows[1].Changes);
    }

    [Fact]
    public async Task QueryAsync_ReturnsNewestFirst_AndPagesByBeforeId()
    {
        for (var i = 0; i < 5; i++)
        {
            await _store.AppendAsync(Event(T0.AddMinutes(i), resourceId: $"r{i}"));
        }

        var page1 = await _store.QueryAsync(new AuditEventFilter(), null, 2);
        var page2 = await _store.QueryAsync(new AuditEventFilter(), page1[^1].Id, 2);

        Assert.Equal(["r4", "r3"], page1.Select(e => e.ResourceId));
        Assert.Equal(["r2", "r1"], page2.Select(e => e.ResourceId));
    }

    [Fact]
    public async Task QueryAsync_AppliesEveryFilter()
    {
        var alice = Guid.NewGuid();
        await _store.AppendAsync(Event(T0, "alert", "create", alice));
        await _store.AppendAsync(Event(T0.AddHours(1), "dashboard", "update", alice));
        await _store.AppendAsync(Event(T0.AddHours(2), "alert", "delete", Guid.NewGuid()));

        Assert.Equal(2, (await _store.QueryAsync(new AuditEventFilter { ResourceType = "alert" }, null, 10)).Count);
        Assert.Single(await _store.QueryAsync(new AuditEventFilter { Action = "delete" }, null, 10));
        Assert.Equal(2, (await _store.QueryAsync(new AuditEventFilter { ActorId = alice }, null, 10)).Count);
        Assert.Single(await _store.QueryAsync(new AuditEventFilter { From = T0.AddMinutes(30), To = T0.AddMinutes(90) }, null, 10));
    }

    [Fact]
    public async Task PruneAsync_DeletesOnlyEventsOlderThanTheCutoff()
    {
        await _store.AppendAsync(Event(T0.AddDays(-10)));
        await _store.AppendAsync(Event(T0));

        var deleted = await _store.PruneAsync(T0.AddDays(-1));

        Assert.Equal(1, deleted);
        Assert.Equal(T0, Assert.Single(await _store.QueryAsync(new AuditEventFilter(), null, 10)).Timestamp);
    }

    [Fact]
    public async Task Rows_CannotBeUpdated()
    {
        await _store.AppendAsync(Event(T0));

        await using var connection = await _database.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AuditEvents SET ActorName = 'mallory'";

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }
}
