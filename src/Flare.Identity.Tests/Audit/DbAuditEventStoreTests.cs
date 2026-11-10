using Flare.Identity.Audit;
using Flare.Identity.Tests.TestSupport;
using System.Data.Common;
using Xunit;

namespace Flare.Identity.Tests.Audit;

public class DbAuditEventStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityTestDatabase _database = new();
    private DbAuditEventStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _store = new DbAuditEventStore(_database.ConnectionFactory);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private static NewAuditEvent Event(DateTimeOffset at, string resourceType = "alert", string action = "update", Guid? actor = null, string? resourceId = "r1") =>
        new(at, actor, "admin", "session", action, resourceType, resourceId, "PUT /api/alerts/{id:guid}", 204, "10.0.0.1");

    [Fact]
    public async Task AppendAsync_ThenQueryAsync_RoundTripsEveryField()
    {
        var actor = Guid.NewGuid();
        await _store.AppendAsync(Event(T0, actor: actor), TestContext.Current.CancellationToken);

        var row = Assert.Single(await _store.QueryAsync(new AuditEventFilter(), null, 10, TestContext.Current.CancellationToken));

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
        await _store.AppendAsync(Event(T0) with { Changes = changes }, TestContext.Current.CancellationToken);
        await _store.AppendAsync(Event(T0.AddMinutes(1)), TestContext.Current.CancellationToken);

        var rows = await _store.QueryAsync(new AuditEventFilter(), null, 10, TestContext.Current.CancellationToken);

        Assert.Null(rows[0].Changes);
        Assert.Equal(changes, rows[1].Changes);
    }

    [Fact]
    public async Task QueryAsync_ReturnsNewestFirst_AndPagesByBeforeId()
    {
        for (var i = 0; i < 5; i++)
        {
            await _store.AppendAsync(Event(T0.AddMinutes(i), resourceId: $"r{i}"), TestContext.Current.CancellationToken);
        }

        var page1 = await _store.QueryAsync(new AuditEventFilter(), null, 2, TestContext.Current.CancellationToken);
        var page2 = await _store.QueryAsync(new AuditEventFilter(), page1[^1].Id, 2, TestContext.Current.CancellationToken);

        Assert.Equal(["r4", "r3"], page1.Select(e => e.ResourceId));
        Assert.Equal(["r2", "r1"], page2.Select(e => e.ResourceId));
    }

    [Fact]
    public async Task QueryAsync_AppliesEveryFilter()
    {
        var alice = Guid.NewGuid();
        await _store.AppendAsync(Event(T0, "alert", "create", alice), TestContext.Current.CancellationToken);
        await _store.AppendAsync(Event(T0.AddHours(1), "dashboard", "update", alice), TestContext.Current.CancellationToken);
        await _store.AppendAsync(Event(T0.AddHours(2), "alert", "delete", Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(2, (await _store.QueryAsync(new AuditEventFilter { ResourceType = "alert" }, null, 10, TestContext.Current.CancellationToken)).Count);
        Assert.Single(await _store.QueryAsync(new AuditEventFilter { Action = "delete" }, null, 10, TestContext.Current.CancellationToken));
        Assert.Equal(2, (await _store.QueryAsync(new AuditEventFilter { ActorId = alice }, null, 10, TestContext.Current.CancellationToken)).Count);
        Assert.Single(await _store.QueryAsync(new AuditEventFilter { From = T0.AddMinutes(30), To = T0.AddMinutes(90) }, null, 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PruneAsync_DeletesOnlyEventsOlderThanTheCutoff()
    {
        await _store.AppendAsync(Event(T0.AddDays(-10)), TestContext.Current.CancellationToken);
        await _store.AppendAsync(Event(T0), TestContext.Current.CancellationToken);

        var deleted = await _store.PruneAsync(T0.AddDays(-1), TestContext.Current.CancellationToken);

        Assert.Equal(1, deleted);
        Assert.Equal(T0, Assert.Single(await _store.QueryAsync(new AuditEventFilter(), null, 10, TestContext.Current.CancellationToken)).Timestamp);
    }

    [Fact]
    public async Task Rows_CannotBeUpdated()
    {
        await _store.AppendAsync(Event(T0), TestContext.Current.CancellationToken);

        await using var connection = await _database.ConnectionFactory.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AuditEvents SET ActorName = 'mallory'";

        await Assert.ThrowsAnyAsync<DbException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AppendAsync_ChainsEachRowToThePreviousHash()
    {
        await _store.AppendAsync(Event(T0), TestContext.Current.CancellationToken);
        await _store.AppendAsync(Event(T0.AddMinutes(1)), TestContext.Current.CancellationToken);

        var rows = await _store.QueryAsync(new AuditEventFilter(), null, 10, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, rows[1].PrevHash);
        Assert.Equal(rows[1].Hash, rows[0].PrevHash);
        Assert.NotEqual(rows[1].Hash, rows[0].Hash);
    }

    [Fact]
    public async Task VerifyChainAsync_AcceptsAnUntamperedChain_AndAfterRetentionPrune()
    {
        for (var i = 0; i < 5; i++)
        {
            await _store.AppendAsync(Event(T0.AddDays(i)), TestContext.Current.CancellationToken);
        }

        var full = await _store.VerifyChainAsync(TestContext.Current.CancellationToken);
        await _store.PruneAsync(T0.AddDays(2), TestContext.Current.CancellationToken);
        var pruned = await _store.VerifyChainAsync(TestContext.Current.CancellationToken);

        Assert.True(full.Valid);
        Assert.Equal(5, full.ChainedEvents);
        Assert.True(pruned.Valid);
        Assert.Equal(3, pruned.ChainedEvents);
        Assert.Equal(full.HeadHash, pruned.HeadHash);
    }

    [Fact]
    public async Task VerifyChainAsync_DetectsEditedContent()
    {
        for (var i = 0; i < 3; i++)
        {
            await _store.AppendAsync(Event(T0.AddMinutes(i)), TestContext.Current.CancellationToken);
        }

        var target = (await _store.QueryAsync(new AuditEventFilter(), null, 10, TestContext.Current.CancellationToken))[1];
        // The no-update trigger has to go first; Postgres names the table in DROP TRIGGER, SQLite does not.
        var dropTrigger = _database.Provider == IdentityProvider.Postgres
            ? "DROP TRIGGER IF EXISTS TR_AuditEvents_NoUpdate ON AuditEvents"
            : "DROP TRIGGER IF EXISTS TR_AuditEvents_NoUpdate";
        await TamperAsync($"{dropTrigger}; UPDATE AuditEvents SET ActorName = 'mallory' WHERE Id = {target.Id}");

        var result = await _store.VerifyChainAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Valid);
        Assert.Equal(target.Id, result.FirstBrokenId);
    }

    [Fact]
    public async Task VerifyChainAsync_DetectsARemovedMiddleRow()
    {
        for (var i = 0; i < 3; i++)
        {
            await _store.AppendAsync(Event(T0.AddMinutes(i)), TestContext.Current.CancellationToken);
        }

        var rows = await _store.QueryAsync(new AuditEventFilter(), null, 10, TestContext.Current.CancellationToken);
        await TamperAsync($"DELETE FROM AuditEvents WHERE Id = {rows[1].Id}");

        var result = await _store.VerifyChainAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Valid);
        Assert.Equal(rows[0].Id, result.FirstBrokenId);
    }

    [Fact]
    public async Task VerifyChainAsync_CountsRowsWithoutAHashAsLegacy()
    {
        await TamperAsync(
            "INSERT INTO AuditEvents (Timestamp, ActorName, ActorKind, Action, ResourceType, Route, StatusCode) "
            + "VALUES ('2026-01-01T00:00:00.0000000+00:00', 'old', 'session', 'update', 'alert', 'PUT /x', 204)");
        await _store.AppendAsync(Event(T0), TestContext.Current.CancellationToken);

        var result = await _store.VerifyChainAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Valid);
        Assert.Equal(1, result.LegacyEvents);
        Assert.Equal(1, result.ChainedEvents);
    }

    [Fact]
    public async Task VerifyChainAsync_OnAnEmptyTable_IsValid()
    {
        var result = await _store.VerifyChainAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Valid);
        Assert.Equal(0, result.ChainedEvents);
        Assert.Null(result.HeadHash);
    }

    [Fact]
    public async Task QueryAfterAsync_ReturnsOldestFirstAfterTheCursor()
    {
        for (var i = 0; i < 4; i++)
        {
            await _store.AppendAsync(Event(T0.AddMinutes(i), resourceId: $"r{i}"), TestContext.Current.CancellationToken);
        }

        var first = (await _store.QueryAfterAsync(0, 2, TestContext.Current.CancellationToken)).ToList();
        var next = await _store.QueryAfterAsync(first[^1].Id, 10, TestContext.Current.CancellationToken);

        Assert.Equal(["r0", "r1"], first.Select(e => e.ResourceId));
        Assert.Equal(["r2", "r3"], next.Select(e => e.ResourceId));
    }

    [Fact]
    public async Task ForwardingCursor_DefaultsToZero_AndUpserts()
    {
        Assert.Equal(0, await _store.GetForwardingCursorAsync("webhook", TestContext.Current.CancellationToken));

        await _store.SetForwardingCursorAsync("webhook", 5, TestContext.Current.CancellationToken);
        await _store.SetForwardingCursorAsync("webhook", 9, TestContext.Current.CancellationToken);
        await _store.SetForwardingCursorAsync("syslog", 2, TestContext.Current.CancellationToken);

        Assert.Equal(9, await _store.GetForwardingCursorAsync("webhook", TestContext.Current.CancellationToken));
        Assert.Equal(2, await _store.GetForwardingCursorAsync("syslog", TestContext.Current.CancellationToken));
    }

    private async Task TamperAsync(string sql)
    {
        await using var connection = await _database.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
