using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Api.Slos;
using Microsoft.Extensions.Caching.Memory;

namespace Flare.Api.Status;

public interface IPublicStatusService
{
    /// <summary>The public view of the page published at <paramref name="slug"/>, or null when there is none.</summary>
    Task<PublicStatusPage?> GetAsync(string slug, CancellationToken cancellationToken);
}

/// <summary>
/// Builds the unauthenticated status page from the monitors and SLOs behind its components. The result is
/// cached for <see cref="CacheTtl"/> per slug, so anonymous traffic costs a bounded number of ClickHouse
/// reads however many visitors there are. See <c>docs-internal/adr/0158-status-pages.md</c>.
/// </summary>
public sealed class PublicStatusService(
    IStatusPageQueryService pages,
    IStatusIncidentQueryService incidents,
    ISyntheticMonitorQueryService monitors,
    ISloQueryService slos,
    IMemoryCache cache,
    TimeProvider timeProvider) : IPublicStatusService
{
    internal static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task<PublicStatusPage?> GetAsync(string slug, CancellationToken cancellationToken)
    {
        // Caching a miss too keeps a scanner guessing slugs from reaching ClickHouse on every request.
        var key = "status-page:" + slug;
        if (cache.TryGetValue(key, out PublicStatusPage? cached))
        {
            return cached;
        }

        // The page is the same for every visitor and is cached, so it must not inherit a signed-in
        // member's project scope (ProjectScopeMiddleware) and then serve their narrowed SLO counts to everyone.
        var callerScope = ServiceScope.Current;
        ServiceScope.Current = null;
        try
        {
            var page = await pages.GetPublishedBySlugAsync(slug, cancellationToken);
            var built = page is null ? null : await BuildAsync(page, cancellationToken);
            cache.Set(key, built, CacheTtl);
            return built;
        }
        finally
        {
            ServiceScope.Current = callerScope;
        }
    }

    private async Task<PublicStatusPage> BuildAsync(StatusPage page, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var since = new DateTimeOffset(today.AddDays(1 - StatusEvaluator.HistoryDays).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var allMonitors = page.Components.Any(c => c.Kind == StatusComponentKind.Monitor)
            ? (await monitors.ListAsync(cancellationToken)).ToDictionary(m => m.Id)
            : [];
        var statuses = allMonitors.Count > 0 ? await monitors.LatestStatusesAsync(cancellationToken) : null;
        var uptime = allMonitors.Count > 0
            ? await monitors.DailyUptimeAsync(
                page.Components.Where(c => c.Kind == StatusComponentKind.Monitor && allMonitors.ContainsKey(c.RefId)).Select(c => allMonitors[c.RefId].Name).Distinct().ToList(),
                since,
                cancellationToken)
            : null;
        var allSlos = page.Components.Any(c => c.Kind == StatusComponentKind.Slo)
            ? (await slos.ListAsync(cancellationToken)).ToDictionary(s => s.Id)
            : [];

        var components = new List<PublicStatusComponent>();
        foreach (var component in page.Components)
        {
            components.Add(component.Kind == StatusComponentKind.Monitor
                ? MonitorComponent(component, allMonitors, statuses, uptime, today, now)
                : await SloComponentAsync(component, allSlos, today, cancellationToken));
        }

        var published = StatusIncidents.ForPublic(await incidents.ListAsync(page.Id, cancellationToken), page.Components, now);
        return new PublicStatusPage(page.Title, page.Description, StatusEvaluator.Overall(components.Select(c => c.State).ToList()), now, components, published);
    }

    private static PublicStatusComponent MonitorComponent(
        StatusPageComponent component,
        Dictionary<Guid, SyntheticMonitor> allMonitors,
        IReadOnlyDictionary<string, IReadOnlyList<SyntheticLocationStatus>>? statuses,
        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, double>>? uptime,
        DateOnly today,
        DateTimeOffset now)
    {
        // A component whose monitor was deleted stays on the page as Unknown instead of disappearing silently.
        if (!allMonitors.TryGetValue(component.RefId, out var monitor))
        {
            return Unknown(component, today);
        }

        var locations = statuses is not null && statuses.TryGetValue(monitor.Name, out var found) ? found : [];
        var byDay = uptime is not null && uptime.TryGetValue(monitor.Name, out var days) ? days : new Dictionary<DateOnly, double>();
        var history = StatusEvaluator.Days(byDay, today);
        return new PublicStatusComponent(component.Name, StatusEvaluator.MonitorState(monitor, locations, now), StatusEvaluator.UptimePercent(history), history);
    }

    private async Task<PublicStatusComponent> SloComponentAsync(StatusPageComponent component, Dictionary<Guid, Slo> allSlos, DateOnly today, CancellationToken cancellationToken)
    {
        if (!allSlos.TryGetValue(component.RefId, out var slo))
        {
            return Unknown(component, today);
        }

        var status = await slos.GetStatusAsync(slo, cancellationToken);
        var byDay = status.Series
            .GroupBy(p => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(p.TimeUnixMs).UtcDateTime))
            .Where(g => g.Sum(p => p.Total) > 0)
            .ToDictionary(g => g.Key, g => SloCalculator.Sli(g.Sum(p => p.Total), g.Sum(p => p.Bad))!.Value);
        var history = StatusEvaluator.Days(byDay, today);
        return new PublicStatusComponent(component.Name, StatusEvaluator.SloState(status.ErrorBudgetRemaining), status.Sli is { } sli ? Math.Round(sli, 3) : null, history);
    }

    private static PublicStatusComponent Unknown(StatusPageComponent component, DateOnly today) =>
        new(component.Name, StatusState.Unknown, null, StatusEvaluator.Days(new Dictionary<DateOnly, double>(), today));
}
