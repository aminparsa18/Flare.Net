<script lang="ts">
	// The pod counterpart of EventHostMetrics: "was this pod starved or near its limits when
	// this happened". Charts the pod's CPU (cores) and memory working set from the OTel
	// `kubeletstats` receiver (POST /api/pods/metrics) over a window centred on the log,
	// plus CPU/memory as a % of the pod's limits when the collector sends those opt-in
	// metrics - the "against its limits" view is the one that explains throttling/OOM kills.
	import HostMetricChart from '$lib/components/hosts/HostMetricChart.svelte';
	import { Spinner } from '$lib/components/ui/spinner';
	import { getPodMetrics, type PodMetricsPoint, type PodMetricsResponse } from '$lib/pods-api';
	import * as m from '$lib/paraglide/messages';

	interface Props {
		podName: string;
		/** `k8s.namespace.name`, when the log carries it - narrows the match. */
		namespace: string | null;
		/** The log's timestamp (ISO) - the window is centred on it. */
		timestamp: string;
	}

	let { podName, namespace, timestamp }: Props = $props();

	/** Same ±15m as EventHostMetrics. */
	const HALF_WINDOW_MINUTES = 15;

	const logMs = $derived(new Date(timestamp).getTime());
	const fromMs = $derived(logMs - HALF_WINDOW_MINUTES * 60_000);
	const toMs = $derived(logMs + HALF_WINDOW_MINUTES * 60_000);

	let detail = $state.raw<PodMetricsResponse | null>(null);
	let loading = $state(false);
	let error = $state<string | null>(null);

	$effect(() => {
		const query = { podName, namespace: namespace ?? undefined, windowMinutes: HALF_WINDOW_MINUTES * 2, endMs: toMs };
		const abort = new AbortController();
		detail = null;
		error = null;
		loading = true;
		getPodMetrics(query, abort.signal)
			.then((response) => {
				if (!abort.signal.aborted) detail = response;
			})
			.catch((err) => {
				if (!abort.signal.aborted) error = err instanceof Error ? err.message : String(err);
			})
			.finally(() => {
				if (!abort.signal.aborted) loading = false;
			});
		return () => abort.abort();
	});

	function series(valueOf: (p: PodMetricsPoint) => number | null) {
		return (detail?.points ?? []).map((p) => ({ time: new Date(p.bucketStart).getTime(), value: valueOf(p) }));
	}

	function has(valueOf: (p: PodMetricsPoint) => number | null): boolean {
		return (detail?.points ?? []).some((p) => valueOf(p) != null);
	}

	// Usage charts always show once there's any data (an empty one says "no data"); the
	// limit charts only when the collector sends them, since most setups don't.
	const charts = $derived.by(() => {
		if (!has((p) => p.cpuCores) && !has((p) => p.memoryWorkingSetBytes)) return [];
		const list = [
			{ label: m.eventDetail_podCpuCores(), unit: null, points: series((p) => p.cpuCores) },
			{ label: m.eventDetail_podMemoryWorkingSet(), unit: 'By', points: series((p) => p.memoryWorkingSetBytes) }
		];
		if (has((p) => p.cpuLimitPercent)) list.push({ label: m.eventDetail_podCpuOfLimit(), unit: '%', points: series((p) => p.cpuLimitPercent) });
		if (has((p) => p.memoryLimitPercent)) list.push({ label: m.eventDetail_podMemoryOfLimit(), unit: '%', points: series((p) => p.memoryLimitPercent) });
		return list;
	});
</script>

<section class="flex flex-col gap-2">
	<h3 class="text-sm font-medium">
		{m.eventDetail_podMetrics()}
		<span class="text-muted-foreground font-mono font-normal">· {namespace ? `${namespace}/` : ''}{podName}</span>
	</h3>
	{#if loading}
		<div class="flex h-24 items-center justify-center"><Spinner /></div>
	{:else if error}
		<p class="text-destructive text-xs">{error}</p>
	{:else if charts.length === 0}
		<p class="text-muted-foreground text-xs">{m.eventDetail_podMetricsNoData()}</p>
	{:else}
		<div class="grid gap-3 md:grid-cols-2">
			{#each charts as chart (chart.label)}
				<HostMetricChart label={chart.label} unit={chart.unit} points={chart.points} {fromMs} {toMs} markerMs={logMs} />
			{/each}
		</div>
	{/if}
</section>
