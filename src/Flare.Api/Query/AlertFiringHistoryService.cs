using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

/// <summary>One change in a rule's firing state: it fired (<see cref="Resolved"/> false) or recovered (true) at <see cref="At"/>.</summary>
public sealed record AlertTransition(Guid RuleId, string RuleName, DateTimeOffset At, bool Resolved);

public interface IAlertFiringHistoryService
{
    /// <summary>
    /// Fires and resolutions in <c>[from, to)</c>, plus each rule's last one before <paramref name="from"/> so the caller
    /// knows which rules were already firing when the range opened. Suppressed fires and escalations are not fires.
    /// <paramref name="ruleIds"/> narrows to those rules; null is every rule, including deleted ones.
    /// </summary>
    Task<IReadOnlyList<AlertTransition>> GetTransitionsAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<Guid>? ruleIds, CancellationToken cancellationToken);
}

/// <summary>The ClickHouse seam for the alert annotations Prometheus overlay (ADR-0252). Reads <c>alert_events</c> only.</summary>
public sealed class AlertFiringHistoryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits) : IAlertFiringHistoryService
{
    public async Task<IReadOnlyList<AlertTransition>> GetTransitionsAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<Guid>? ruleIds, CancellationToken cancellationToken)
    {
        if (ruleIds is { Count: 0 })
        {
            return [];
        }

        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", from.UtcDateTime);
        parameters.AddParameter("to", to.UtcDateTime);
        var ruleFilter = "";
        if (ruleIds is not null)
        {
            parameters.AddParameter("ruleIds", ruleIds.ToArray());
            ruleFilter = " AND RuleId IN {ruleIds:Array(UUID)}";
        }

        // The same fire definition as AlertQueryService.GetLastFiredAsync: not an escalation, and not a suppressed fire
        // (a maintenance window swallowed it, nobody was told). Resolution rows have Resolved = 1 and are never suppressed.
        var sql = $$"""
            SELECT RuleId, RuleName, FiredAt, Resolved
            FROM alert_events
            WHERE FiredAt >= {from:DateTime64(3)} AND FiredAt < {to:DateTime64(3)} AND Escalated = 0 AND NotificationStatus != 'Suppressed'{{ruleFilter}}
            UNION ALL
            SELECT RuleId, argMax(RuleName, FiredAt), max(FiredAt), argMax(Resolved, FiredAt)
            FROM alert_events
            WHERE FiredAt < {from:DateTime64(3)} AND Escalated = 0 AND NotificationStatus != 'Suppressed'{{ruleFilter}}
            GROUP BY RuleId
            """;

        await using var reader = await client.ExecuteReaderAsync(sql, parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
        var transitions = new List<AlertTransition>();
        while (reader.Read())
        {
            transitions.Add(new AlertTransition(
                reader.GetGuid(0),
                reader.GetString(1),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)),
                Convert.ToByte(reader.GetValue(3)) != 0));
        }

        return transitions;
    }
}
