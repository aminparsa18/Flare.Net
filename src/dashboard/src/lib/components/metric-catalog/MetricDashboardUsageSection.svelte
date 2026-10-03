<script lang="ts">
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { getMetricDashboardUsage, type MetricDashboardUsage } from '$lib/metric-catalog-api';
	import { dashboardPath } from '$lib/dashboards/page-paths';
	import * as m from '$lib/paraglide/messages';

	let { metricName }: { metricName: string } = $props();

	let usage = $state.raw<MetricDashboardUsage[] | null>(null);
	let error = $state<string | null>(null);

	// Re-fetches when the sheet switches to a related metric; stale responses are aborted.
	$effect(() => {
		const name = metricName;
		const abort = new AbortController();
		usage = null;
		error = null;
		getMetricDashboardUsage(name, abort.signal)
			.then((result) => (usage = result))
			.catch((err) => {
				if (!abort.signal.aborted) error = err instanceof Error ? err.message : String(err);
			});
		return () => abort.abort();
	});
</script>

<section>
	<h3 class="text-sm font-medium">{m.metricDashboards_heading()}</h3>
	{#if error}
		<p class="text-destructive mt-1 text-xs">{m.metricDashboards_error({ message: error })}</p>
	{:else if usage === null}
		<div class="mt-2"><Spinner /></div>
	{:else if usage.length === 0}
		<p class="text-muted-foreground mt-1 text-xs">{m.metricDashboards_none()}</p>
	{:else}
		<p class="text-muted-foreground mt-1 text-xs">{m.metricDashboards_hint()}</p>
		<ul class="mt-2 divide-y rounded-md border">
			{#each usage as dashboard (dashboard.dashboardId)}
				<li class="px-3 py-2">
					<a class="text-sm font-medium hover:underline" href={dashboardPath({ id: dashboard.dashboardId })}>{dashboard.dashboardName}</a>
					<ul class="mt-1 flex flex-wrap gap-1">
						{#each dashboard.panels as panel, i (`${panel.panelId}-${i}`)}
							<li>
								<Badge variant="secondary" class="max-w-60 truncate">
									{panel.title || '—'}{#if panel.inFormula} · {m.metricDashboards_formula()}{/if}
								</Badge>
							</li>
						{/each}
					</ul>
				</li>
			{/each}
		</ul>
	{/if}
</section>
