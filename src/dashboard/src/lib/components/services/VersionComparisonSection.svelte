<script lang="ts">
	// "Did my deploy break anything?" for one service, in the Services tab's per-service drill-down
	// (ServiceCallBreakdownDialog). Compares two `service.version` values - by default the newest
	// against the one first seen before it - on endpoint error rate and p95, and lists what is new
	// under the current version: exception types, outbound dependencies and Drain log patterns.
	// The comparison is server-side (Flare.Api's VersionComparisonQueryBuilder); this renders and links.
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import * as Table from '$lib/components/ui/table';
	import { formatMs, formatPercent } from '$lib/indexing/format';
	import { buildServiceVersionErrorsHref, buildServiceVersionLogsHref, buildServiceVersionTracesHref } from '$lib/deep-links';
	import { getVersionComparison, type VersionComparisonResponse, type VersionEndpointComparison } from '$lib/version-comparison-api';
	import { formatDateTimeMinutes } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	let { service }: { service: string } = $props();

	const LOOKBACK_HOURS = 168;
	const MAX_ENDPOINT_ROWS = 15;

	let baselineChoice = $state<string | null>(null);
	let currentChoice = $state<string | null>(null);
	let result = $state.raw<VersionComparisonResponse | null>(null);
	let loading = $state(false);
	let error = $state<string | null>(null);

	// A different service starts over from the default pair.
	$effect(() => {
		void service;
		baselineChoice = null;
		currentChoice = null;
	});

	$effect(() => {
		const controller = new AbortController();
		loading = true;
		error = null;

		getVersionComparison(service, baselineChoice, currentChoice, LOOKBACK_HOURS, controller.signal)
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

	function errorRate(stats: { count: number; errorCount: number } | null): number {
		return stats && stats.count > 0 ? stats.errorCount / stats.count : 0;
	}

	/** Error rate up by a point or more, or p95 up by a quarter and at least 20 ms. */
	function regressed(e: VersionEndpointComparison): boolean {
		if (!e.baseline || !e.current) return false;
		const p95Up = e.current.p95DurationMs >= e.baseline.p95DurationMs * 1.25 && e.current.p95DurationMs - e.baseline.p95DurationMs >= 20;
		return errorRate(e.current) - errorRate(e.baseline) >= 0.01 || p95Up;
	}

	const endpointRows = $derived(
		[...(result?.endpoints ?? [])]
			.sort((a, b) => Number(regressed(b)) - Number(regressed(a)))
			.slice(0, MAX_ENDPOINT_ROWS)
	);

	/** Epoch-ms window the current version was seen in, for the click-through links. */
	const currentWindow = $derived.by(() => {
		const info = result?.versions.find((v) => v.version === result?.currentVersion);
		return info ? { from: info.firstSeenUnixMs, to: info.lastSeenUnixMs + 60_000 } : null;
	});

	function severityVariant(n: number): 'destructive' | 'warning' | 'outline' {
		return n >= 17 ? 'destructive' : n >= 13 ? 'warning' : 'outline';
	}
</script>

<div>
	<h3 class="text-muted-foreground mb-1 text-sm font-medium">{m.versionComparison_heading()}</h3>
	{#if loading && !result}
		<div class="flex justify-center py-4">
			<Spinner class="size-5" />
		</div>
	{:else if error}
		<p class="text-destructive text-sm">{m.versionComparison_loadError()}: {error}</p>
	{:else if result}
		{#if result.versions.length === 0}
			<p class="text-muted-foreground text-sm">{m.versionComparison_noVersions({ hours: result.lookbackHours })}</p>
		{:else if !result.baselineVersion || !result.currentVersion}
			<p class="text-muted-foreground text-sm">
				{m.versionComparison_oneVersion({ version: result.versions[0].version, hours: result.lookbackHours })}
			</p>
		{:else}
			<div class="mb-2 flex flex-wrap items-center gap-2 text-sm">
				<select
					class="border-input bg-background h-8 rounded-md border px-2 text-sm"
					aria-label={m.versionComparison_baseline()}
					value={result.baselineVersion}
					onchange={(e) => (baselineChoice = e.currentTarget.value)}
				>
					{#each result.versions as v (v.version)}
						<option value={v.version}>{v.version}</option>
					{/each}
				</select>
				<span class="text-muted-foreground">→</span>
				<select
					class="border-input bg-background h-8 rounded-md border px-2 text-sm"
					aria-label={m.versionComparison_current()}
					value={result.currentVersion}
					onchange={(e) => (currentChoice = e.currentTarget.value)}
				>
					{#each result.versions as v (v.version)}
						<option value={v.version}>{v.version}</option>
					{/each}
				</select>
				{#if loading}
					<Spinner class="size-4" />
				{/if}
			</div>
			<p class="text-muted-foreground mb-3 text-xs">
				{m.versionComparison_description({ hours: result.lookbackHours })}
				{#if currentWindow}
					{m.versionComparison_currentSeen({ since: formatDateTimeMinutes(currentWindow.from) })}
				{/if}
			</p>

			<h4 class="mb-1 text-sm font-medium">{m.versionComparison_endpointsHeading()}</h4>
			{#if endpointRows.length === 0}
				<p class="text-muted-foreground mb-3 text-sm">{m.versionComparison_endpointsEmpty()}</p>
			{:else}
				<Table.Root class="mb-3">
					<Table.Header>
						<Table.Row>
							<Table.Head>{m.versionComparison_endpointColumn()}</Table.Head>
							<Table.Head class="text-right">{m.versionComparison_callsColumn()}</Table.Head>
							<Table.Head class="text-right">{m.versionComparison_errorRateColumn()}</Table.Head>
							<Table.Head class="text-right">{m.versionComparison_p95Column()}</Table.Head>
						</Table.Row>
					</Table.Header>
					<Table.Body>
						{#each endpointRows as e (e.endpoint)}
							<Table.Row>
								<Table.Cell class="font-medium">
									{#if currentWindow}
										<a
											class="hover:underline"
											href={buildServiceVersionTracesHref(service, result.currentVersion, currentWindow.from, currentWindow.to, e.endpoint)}
										>
											{e.endpoint}
										</a>
									{:else}
										{e.endpoint}
									{/if}
									{#if !e.baseline}
										<Badge variant="outline" class="ml-1">{m.versionComparison_newEndpoint()}</Badge>
									{:else if !e.current}
										<Badge variant="outline" class="ml-1">{m.versionComparison_goneEndpoint()}</Badge>
									{:else if regressed(e)}
										<Badge variant="destructive" class="ml-1">{m.versionComparison_regressed()}</Badge>
									{/if}
								</Table.Cell>
								<Table.Cell class="text-right tabular-nums">{e.baseline?.count ?? '–'} → {e.current?.count ?? '–'}</Table.Cell>
								<Table.Cell class="text-right tabular-nums">
									{e.baseline ? formatPercent(errorRate(e.baseline) * 100) : '–'} → {e.current ? formatPercent(errorRate(e.current) * 100) : '–'}
								</Table.Cell>
								<Table.Cell class="text-right tabular-nums">
									{e.baseline ? formatMs(e.baseline.p95DurationMs) : '–'} → {e.current ? formatMs(e.current.p95DurationMs) : '–'}
								</Table.Cell>
							</Table.Row>
						{/each}
					</Table.Body>
				</Table.Root>
			{/if}

			<h4 class="mb-1 text-sm font-medium">{m.versionComparison_newExceptionsHeading()}</h4>
			{#if result.newExceptions.length === 0}
				<p class="text-muted-foreground mb-3 text-sm">{m.versionComparison_noneNew()}</p>
			{:else}
				<ul class="mb-3 space-y-1 text-sm">
					{#each result.newExceptions as ex (ex.exceptionType)}
						<li class="flex items-center gap-2">
							<a
								class="text-primary font-mono text-xs hover:underline"
								href={buildServiceVersionErrorsHref(service, result.currentVersion, ex.exceptionType, ex.firstSeenUnixMs - 3_600_000, ex.firstSeenUnixMs + 86_400_000 * 7)}
							>
								{ex.exceptionType}
							</a>
							<span class="text-muted-foreground ml-auto text-xs tabular-nums">{m.versionComparison_occurrences({ count: ex.count })}</span>
						</li>
					{/each}
				</ul>
			{/if}

			<h4 class="mb-1 text-sm font-medium">{m.versionComparison_newDependenciesHeading()}</h4>
			{#if result.newDependencies.length === 0}
				<p class="text-muted-foreground mb-3 text-sm">{m.versionComparison_noneNew()}</p>
			{:else}
				<ul class="mb-3 space-y-1 text-sm">
					{#each result.newDependencies as d (d.kind + d.target)}
						<li class="flex items-center gap-2">
							<Badge variant="outline">{d.kind === 'Database' ? m.versionComparison_kindDatabase() : m.versionComparison_kindExternal()}</Badge>
							<span class="font-medium">{d.target}</span>
							<span class="text-muted-foreground ml-auto text-xs tabular-nums">
								{m.versionComparison_calls({ count: d.callCount })}{d.errorCount > 0 ? ` · ${m.versionComparison_errors({ count: d.errorCount })}` : ''}
							</span>
						</li>
					{/each}
				</ul>
			{/if}

			<h4 class="mb-1 flex items-center gap-2 text-sm font-medium">
				{m.versionComparison_newPatternsHeading()}
				{#if currentWindow}
					<a class="text-primary text-xs font-normal hover:underline" href={buildServiceVersionLogsHref(service, result.currentVersion, currentWindow.from, currentWindow.to)}>
						{m.versionComparison_logsLink()}
					</a>
				{/if}
			</h4>
			{#if result.newLogPatterns.length === 0}
				<p class="text-muted-foreground text-sm">{m.versionComparison_noneNew()}</p>
			{:else}
				<ul class="space-y-1 text-sm">
					{#each result.newLogPatterns as p (p.patternId)}
						<li class="flex items-start gap-2">
							<Badge variant={severityVariant(p.maxSeverityNumber)}>{p.maxSeverityNumber >= 17 ? 'ERROR' : p.maxSeverityNumber >= 13 ? 'WARN' : 'INFO'}</Badge>
							<span class="min-w-0 flex-1 truncate font-mono text-xs" title={p.template}>{p.template}</span>
							<span class="text-muted-foreground text-xs tabular-nums">{m.versionComparison_occurrences({ count: p.count })}</span>
						</li>
					{/each}
				</ul>
			{/if}
		{/if}
	{/if}
</div>
