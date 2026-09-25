<script lang="ts">
	// "Was the box starved when this happened": the CPU and memory of the host a log came
	// from, over a window centred on the log's timestamp, with the log's instant marked.
	// Reuses the Hosts page's drill-down query (POST /api/hosts/metrics, anchored with
	// endUnixMs) and chart, so the figures mean exactly what they mean there - derived from
	// OTel `hostmetrics`-receiver metrics keyed by `host.name`.
	import HostMetricChart from '$lib/components/hosts/HostMetricChart.svelte';
	import { Spinner } from '$lib/components/ui/spinner';
	import { getHostMetrics, type HostMetricsPoint, type HostMetricsResponse } from '$lib/hosts-api';
	import * as m from '$lib/paraglide/messages';

	interface Props {
		hostName: string;
		/** The log's timestamp (ISO) - the window is centred on it. */
		timestamp: string;
	}

	let { hostName, timestamp }: Props = $props();

	/** Minutes either side of the log - 30m total, which the server buckets at 60s (the hostmetrics receiver's default scrape interval). */
	const HALF_WINDOW_MINUTES = 15;

	const logMs = $derived(new Date(timestamp).getTime());
	const fromMs = $derived(logMs - HALF_WINDOW_MINUTES * 60_000);
	const toMs = $derived(logMs + HALF_WINDOW_MINUTES * 60_000);

	let detail = $state.raw<HostMetricsResponse | null>(null);
	let loading = $state(false);
	let error = $state<string | null>(null);

	$effect(() => {
		const host = hostName;
		const end = toMs;
		const abort = new AbortController();
		detail = null;
		error = null;
		loading = true;
		getHostMetrics(host, HALF_WINDOW_MINUTES * 2, abort.signal, end)
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

	function series(valueOf: (p: HostMetricsPoint) => number | null) {
		return (detail?.points ?? []).map((p) => ({ time: new Date(p.bucketStart).getTime(), value: valueOf(p) }));
	}

	const hasCpuOrMemory = $derived((detail?.points ?? []).some((p) => p.cpuPercent != null || p.memoryPercent != null));
</script>

<section class="flex flex-col gap-2">
	<h3 class="text-sm font-medium">
		{m.eventDetail_hostMetrics()}
		<span class="text-muted-foreground font-mono font-normal">· {hostName}</span>
	</h3>
	{#if loading}
		<div class="flex h-24 items-center justify-center"><Spinner /></div>
	{:else if error}
		<p class="text-destructive text-xs">{error}</p>
	{:else if !hasCpuOrMemory}
		<p class="text-muted-foreground text-xs">{m.eventDetail_hostMetricsNoData()}</p>
	{:else}
		<div class="grid gap-3 md:grid-cols-2">
			<HostMetricChart label={m.hostsPage_cpuColumn()} unit="%" points={series((p) => p.cpuPercent)} {fromMs} {toMs} markerMs={logMs} />
			<HostMetricChart label={m.hostsPage_memoryColumn()} unit="%" points={series((p) => p.memoryPercent)} {fromMs} {toMs} markerMs={logMs} />
		</div>
	{/if}
</section>
