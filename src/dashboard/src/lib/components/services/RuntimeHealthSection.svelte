<script lang="ts">
	// ".NET runtime health" findings for one service, at the top of the Services tab's
	// per-service drill-down (ServiceCallBreakdownDialog). Findings, not charts: thread-pool
	// starvation, GC pressure, lock-contention and exception spikes, each linking to the
	// traces and logs of the window it covers. Detection is server-side
	// (Flare.Api's RuntimeHealthDetector); this only renders and links.
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { formatPercent } from '$lib/indexing/format';
	import { buildServiceWindowLogsHref, buildServiceWindowTracesHref } from '$lib/deep-links';
	import { getRuntimeHealth, type RuntimeHealthFinding, type RuntimeHealthResponse } from '$lib/runtime-health-api';
	import { formatTimeOfDay } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	let { service, windowMinutes }: { service: string; windowMinutes: number } = $props();

	// Baselines and "flatlined" need context, so a 5-minute Services window still looks back this far.
	const MIN_LOOKBACK_MINUTES = 30;

	let result = $state.raw<RuntimeHealthResponse | null>(null);
	let loading = $state(false);
	let error = $state<string | null>(null);

	$effect(() => {
		const controller = new AbortController();
		loading = true;
		error = null;

		getRuntimeHealth(service, Math.max(windowMinutes, MIN_LOOKBACK_MINUTES), controller.signal)
			.then((response) => {
				if (!controller.signal.aborted) result = response;
			})
			.catch((err) => {
				if (controller.signal.aborted) return;
				result = null;
				error = err instanceof Error ? err.message : String(err);
			})
			.finally(() => {
				if (!controller.signal.aborted) loading = false;
			});

		return () => controller.abort();
	});

	function rate(value: number): string {
		return value >= 100 ? Math.round(value).toString() : value.toFixed(1);
	}

	function title(f: RuntimeHealthFinding): string {
		switch (f.kind) {
			case 'ThreadPoolStarvation':
				return m.runtimeHealth_kindThreadPoolStarvation();
			case 'GcPressure':
				return m.runtimeHealth_kindGcPressure();
			case 'LockContention':
				return m.runtimeHealth_kindLockContention();
			case 'ExceptionRate':
				return m.runtimeHealth_kindExceptionRate();
		}
	}

	function detail(f: RuntimeHealthFinding): string {
		switch (f.kind) {
			case 'ThreadPoolStarvation':
				return f.baseline == null
					? m.runtimeHealth_detailThreadPoolStarvationNoBaseline({ peak: Math.round(f.value) })
					: m.runtimeHealth_detailThreadPoolStarvation({ peak: Math.round(f.value), baseline: rate(f.baseline) });
			case 'GcPressure':
				return m.runtimeHealth_detailGcPressure({ percent: formatPercent(f.value * 100) });
			case 'LockContention':
				return m.runtimeHealth_detailLockContention({ rate: rate(f.value), baseline: rate(f.baseline ?? 0) });
			case 'ExceptionRate':
				return m.runtimeHealth_detailExceptionRate({ rate: rate(f.value), baseline: rate(f.baseline ?? 0) });
		}
	}
</script>

<div>
	<h3 class="text-muted-foreground mb-1 text-sm font-medium">{m.runtimeHealth_heading()}</h3>
	{#if loading && !result}
		<div class="flex justify-center py-4">
			<Spinner class="size-5" />
		</div>
	{:else if error}
		<p class="text-destructive text-sm">{m.runtimeHealth_loadError()}: {error}</p>
	{:else if result}
		{#if !result.hasRuntimeMetrics}
			<p class="text-muted-foreground text-sm">{m.runtimeHealth_noData()}</p>
		{:else}
			<p class="text-muted-foreground mb-2 text-xs">{m.runtimeHealth_description({ minutes: result.windowMinutes })}</p>
			{#if result.findings.length === 0}
				<p class="text-sm">{m.runtimeHealth_healthy()}</p>
			{:else}
				<ul class="space-y-2">
					{#each result.findings as f (f.kind + f.instance + f.startUnixMs)}
						<li class="rounded-md border p-3 text-sm">
							<div class="flex flex-wrap items-center gap-2">
								<Badge variant={f.severity === 'Critical' ? 'destructive' : 'warning'}>
									{f.severity === 'Critical' ? m.runtimeHealth_severityCritical() : m.runtimeHealth_severityWarning()}
								</Badge>
								<span class="font-medium">{title(f)}</span>
								{#if f.ongoing}
									<Badge variant="outline">{m.runtimeHealth_ongoing()}</Badge>
								{/if}
								<span class="text-muted-foreground ml-auto text-xs tabular-nums">
									{formatTimeOfDay(f.startUnixMs, 'minute')}–{formatTimeOfDay(f.endUnixMs, 'minute')}
								</span>
							</div>
							<p class="text-muted-foreground mt-1">{detail(f)}</p>
							<div class="mt-2 flex flex-wrap items-center gap-3 text-xs">
								{#if f.instance}
									<span class="text-muted-foreground">{m.runtimeHealth_instance({ instance: f.instance })}</span>
								{/if}
								<a class="text-primary hover:underline" href={buildServiceWindowTracesHref(service, f.startUnixMs, f.endUnixMs)}>
									{m.runtimeHealth_tracesLink()}
								</a>
								<a class="text-primary hover:underline" href={buildServiceWindowLogsHref(service, f.startUnixMs, f.endUnixMs)}>
									{m.runtimeHealth_logsLink()}
								</a>
							</div>
						</li>
					{/each}
				</ul>
			{/if}
		{/if}
	{/if}
</div>
