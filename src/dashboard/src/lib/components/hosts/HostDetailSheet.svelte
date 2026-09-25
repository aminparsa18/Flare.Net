<script lang="ts">
	import * as Sheet from '$lib/components/ui/sheet';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import HostMetricChart from './HostMetricChart.svelte';
	import { hostsContext } from '$lib/hosts/context';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import type { HostMetricsPoint } from '$lib/hosts-api';
	import * as m from '$lib/paraglide/messages';

	const hosts = hostsContext.get();

	const summary = $derived(hosts.hosts?.find((h) => h.hostName === hosts.selectedHost) ?? null);

	// The window the charts' x-axis spans - ends at the newest bucket's end rather than
	// "now", so a fresh load and a 30s-later poll draw the same axis.
	const range = $derived.by(() => {
		const detail = hosts.detail;
		if (!detail || detail.points.length === 0) return null;
		const toMs = new Date(detail.points.at(-1)!.bucketStart).getTime() + detail.bucketWidthSeconds * 1000;
		return { fromMs: toMs - detail.windowMinutes * 60_000, toMs };
	});

	function series(valueOf: (p: HostMetricsPoint) => number | null) {
		return (hosts.detail?.points ?? []).map((p) => ({ time: new Date(p.bucketStart).getTime(), value: valueOf(p) }));
	}

	const charts = $derived([
		{ label: m.hostsPage_cpuColumn(), unit: '%', points: series((p) => p.cpuPercent) },
		{ label: m.hostsPage_memoryColumn(), unit: '%', points: series((p) => p.memoryPercent) },
		{ label: m.hostsPage_diskColumn(), unit: '%', points: series((p) => p.diskPercent) },
		{ label: m.hostsPage_loadColumn(), unit: null, points: series((p) => p.loadAverage15m) }
	]);
</script>

<Sheet.Root
	open={hosts.selectedHost !== null}
	onOpenChange={(next) => {
		if (!next) hosts.closeHost();
	}}
>
	<Sheet.Content class="flex w-full flex-col sm:max-w-3xl">
		{#if hosts.selectedHost}
			<Sheet.Header>
				<Sheet.Title class="flex flex-wrap items-center gap-2">
					{hosts.selectedHost}
					{#if summary?.osType}
						<Badge variant="outline">{summary.osType}</Badge>
					{/if}
				</Sheet.Title>
				<Sheet.Description>{servicesWindowPresetLabel(hosts.windowPreset)}</Sheet.Description>
			</Sheet.Header>
			<div class="min-h-0 flex-1 overflow-y-auto px-4 pb-8">
				{#if hosts.detailLoading && !hosts.detail}
					<div class="flex h-32 items-center justify-center"><Spinner /></div>
				{:else if hosts.detailError}
					<p class="text-destructive text-sm">{hosts.detailError}</p>
				{:else if !range}
					<p class="text-muted-foreground text-sm">{m.hostsPage_chartNoData()}</p>
				{:else}
					<div class="grid gap-3 md:grid-cols-2">
						{#each charts as chart (chart.label)}
							<HostMetricChart label={chart.label} unit={chart.unit} points={chart.points} fromMs={range.fromMs} toMs={range.toMs} />
						{/each}
					</div>
				{/if}
			</div>
		{/if}
	</Sheet.Content>
</Sheet.Root>
