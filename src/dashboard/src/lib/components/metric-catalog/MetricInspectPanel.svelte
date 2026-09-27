<script lang="ts">
	// The drill-down's "Inspect" view: a few series' raw samples, how time aggregation reduces
	// each to one value per bucket, and how space aggregation merges the series - see
	// MetricInspectReducer.cs for the rules this displays.
	import * as Select from '$lib/components/ui/select';
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { metricCatalogContext } from '$lib/metric-catalog/context';
	import { METRIC_INSPECT_BUCKETS, METRIC_INSPECT_WINDOWS } from '$lib/metric-catalog/state.svelte';
	import type { MetricInspectSample, MetricInspectSampleKind, MetricInspectSeries } from '$lib/metric-catalog-api';
	import { formatTimeOfDay } from '$lib/time/format';
	import { formatContribution, formatSampleValue } from './format';
	import * as m from '$lib/paraglide/messages';

	const ALL_SERVICES = '__all__';

	const catalog = metricCatalogContext.get();

	const result = $derived(catalog.inspect);
	const isGauge = $derived(catalog.selected?.type === 'Gauge');
	const isHistogram = $derived(catalog.selected?.type === 'Histogram' || catalog.selected?.type === 'ExponentialHistogram');
	const services = $derived(catalog.detail?.services.map((s) => s.serviceName) ?? []);

	const KIND_LABEL: Record<MetricInspectSampleKind, () => string> = {
		Level: m.metricInspect_kindLevel,
		Delta: m.metricInspect_kindDelta,
		First: m.metricInspect_kindFirst,
		Difference: m.metricInspect_kindDifference,
		Reset: m.metricInspect_kindReset
	};

	function windowLabel(minutes: number): string {
		return minutes >= 60 ? m.metricInspect_hours({ hours: minutes / 60 }) : m.metricInspect_minutes({ minutes });
	}

	function bucketLabel(seconds: number): string {
		return seconds >= 60 ? m.metricInspect_minutes({ minutes: seconds / 60 }) : m.metricInspect_seconds({ seconds });
	}

	function seriesLabel(series: MetricInspectSeries): string {
		const attributes = Object.entries(series.attributes);
		return attributes.length === 0 ? m.metricInspect_noAttributes() : attributes.map(([k, v]) => `${k}=${v}`).join(', ');
	}

	// Samples grouped under the bucket they fall in - the same epoch-aligned floor the server
	// buckets with, so each group lines up with one entry of series.buckets.
	function samplesByBucket(series: MetricInspectSeries, bucketMs: number): Map<number, MetricInspectSample[]> {
		const groups = new Map<number, MetricInspectSample[]>();
		for (const sample of series.samples) {
			const start = sample.timeMs - (((sample.timeMs % bucketMs) + bucketMs) % bucketMs);
			const group = groups.get(start);
			if (group) group.push(sample);
			else groups.set(start, [sample]);
		}
		return groups;
	}

	// Step 2's matrix: one row per bucket, one column per series.
	const matrix = $derived.by(() => {
		if (!result) return [];
		return result.merged.map((merged) => ({
			merged,
			perSeries: result.series.map((s) => s.buckets.find((b) => b.bucketStartMs === merged.bucketStartMs) ?? null)
		}));
	});
</script>

<div class="space-y-6">
	<div class="flex flex-wrap items-center gap-2">
		<Select.Root
			type="single"
			value={catalog.inspectService ?? ALL_SERVICES}
			onValueChange={(v) => v && catalog.setInspectOptions({ service: v === ALL_SERVICES ? null : v })}
		>
			<Select.Trigger class="w-auto max-w-56">
				<span class="truncate">{catalog.inspectService ?? m.metricInspect_allServices()}</span>
			</Select.Trigger>
			<Select.Content>
				<Select.Item value={ALL_SERVICES} label={m.metricInspect_allServices()} />
				{#each services as service (service)}
					<Select.Item value={service} label={service || '—'} />
				{/each}
			</Select.Content>
		</Select.Root>
		<Select.Root
			type="single"
			value={String(catalog.inspectWindowMinutes)}
			onValueChange={(v) => v && catalog.setInspectOptions({ windowMinutes: Number(v) })}
		>
			<Select.Trigger class="w-auto">{m.metricInspect_windowLabel({ window: windowLabel(catalog.inspectWindowMinutes) })}</Select.Trigger>
			<Select.Content>
				{#each METRIC_INSPECT_WINDOWS as minutes (minutes)}
					<Select.Item value={String(minutes)} label={windowLabel(minutes)} />
				{/each}
			</Select.Content>
		</Select.Root>
		<Select.Root
			type="single"
			value={String(catalog.inspectBucketSeconds)}
			onValueChange={(v) => v && catalog.setInspectOptions({ bucketSeconds: Number(v) })}
		>
			<Select.Trigger class="w-auto">{m.metricInspect_bucketLabel({ bucket: bucketLabel(result?.bucketWidthSeconds ?? catalog.inspectBucketSeconds) })}</Select.Trigger>
			<Select.Content>
				{#each METRIC_INSPECT_BUCKETS as seconds (seconds)}
					<Select.Item value={String(seconds)} label={bucketLabel(seconds)} />
				{/each}
			</Select.Content>
		</Select.Root>
		{#if catalog.inspectLoading && result}
			<Spinner />
		{/if}
	</div>

	<div class="text-muted-foreground space-y-1 text-xs">
		<p>{isGauge ? m.metricInspect_explainGauge() : m.metricInspect_explainCounter()}</p>
		{#if isHistogram}
			<p>{m.metricInspect_histogramNote()}</p>
		{/if}
	</div>

	{#if catalog.inspectLoading && !result}
		<div class="flex h-32 items-center justify-center"><Spinner /></div>
	{:else if catalog.inspectError}
		<p class="text-destructive text-sm">{catalog.inspectError}</p>
	{:else if !result || result.series.length === 0}
		<p class="text-muted-foreground text-sm">{m.metricInspect_noData()}</p>
	{:else}
		{@const bucketMs = result.bucketWidthSeconds * 1000}
		<section class="space-y-2">
			<h3 class="text-sm font-medium">{m.metricInspect_step1Heading()}</h3>
			<p class="text-muted-foreground text-xs">
				{m.metricInspect_seriesShown({ shown: result.series.length, total: result.totalSeriesCount })}
			</p>
			{#each result.series as series, i (`${series.serviceName} ${seriesLabel(series)}`)}
				{@const groups = samplesByBucket(series, bucketMs)}
				<details class="rounded-md border" open={i === 0}>
					<summary class="flex cursor-pointer flex-wrap items-center gap-2 px-3 py-2 text-sm">
						<Badge variant="outline">S{i + 1}</Badge>
						<span class="font-medium">{series.serviceName || '—'}</span>
						<span class="text-muted-foreground min-w-0 flex-1 truncate font-mono text-xs" title={seriesLabel(series)}>{seriesLabel(series)}</span>
						<span class="text-muted-foreground text-xs tabular-nums">{m.metricInspect_sampleCount({ count: series.samples.length })}</span>
						{#if series.samplesTruncated}
							<Badge variant="secondary" title={m.metricInspect_truncatedTitle()}>{m.metricInspect_truncated()}</Badge>
						{/if}
					</summary>
					<div class="max-h-96 overflow-y-auto border-t">
						<Table.Root>
							<Table.Header>
								<Table.Row>
									<Table.Head>{m.metricInspect_timeColumn()}</Table.Head>
									<Table.Head class="text-right">{m.metricInspect_valueColumn()}</Table.Head>
									<Table.Head>{m.metricInspect_howColumn()}</Table.Head>
									<Table.Head class="text-right">{isGauge ? m.metricInspect_bucketAvgColumn() : m.metricInspect_addsColumn()}</Table.Head>
								</Table.Row>
							</Table.Header>
							<Table.Body>
								{#each series.buckets as bucket (bucket.bucketStartMs)}
									<Table.Row class="bg-muted/50 hover:bg-muted/50">
										<Table.Cell class="font-medium tabular-nums" colspan={3}>
											{m.metricInspect_bucketRow({
												start: formatTimeOfDay(bucket.bucketStartMs),
												end: formatTimeOfDay(bucket.bucketStartMs + bucketMs),
												count: bucket.sampleCount
											})}
										</Table.Cell>
										<Table.Cell class="text-right font-medium tabular-nums">{formatSampleValue(bucket.value)}</Table.Cell>
									</Table.Row>
									{#each groups.get(bucket.bucketStartMs) ?? [] as sample, j (j)}
										<Table.Row>
											<Table.Cell class="text-muted-foreground pl-6 tabular-nums">{formatTimeOfDay(sample.timeMs, 'ms')}</Table.Cell>
											<Table.Cell class="text-right tabular-nums">{formatSampleValue(sample.value)}</Table.Cell>
											<Table.Cell class="text-muted-foreground text-xs">{KIND_LABEL[sample.kind]()}</Table.Cell>
											<Table.Cell class="text-muted-foreground text-right tabular-nums">
												{isGauge ? '' : formatContribution(sample.contribution)}
											</Table.Cell>
										</Table.Row>
									{/each}
								{/each}
							</Table.Body>
						</Table.Root>
					</div>
				</details>
			{/each}
		</section>

		<section class="space-y-2">
			<h3 class="text-sm font-medium">{m.metricInspect_step2Heading()}</h3>
			<p class="text-muted-foreground text-xs">{isGauge ? m.metricInspect_step2Gauge() : m.metricInspect_step2Counter()}</p>
			<div class="overflow-x-auto rounded-md border">
				<Table.Root>
					<Table.Header>
						<Table.Row>
							<Table.Head>{m.metricInspect_bucketColumn()}</Table.Head>
							{#each result.series as _, i (i)}
								<Table.Head class="text-right">S{i + 1}</Table.Head>
							{/each}
							<Table.Head class="text-right">{m.metricInspect_mergedColumn()}</Table.Head>
						</Table.Row>
					</Table.Header>
					<Table.Body>
						{#each matrix as row (row.merged.bucketStartMs)}
							<Table.Row>
								<Table.Cell class="tabular-nums">{formatTimeOfDay(row.merged.bucketStartMs)}</Table.Cell>
								{#each row.perSeries as bucket, i (i)}
									<Table.Cell class="text-muted-foreground text-right tabular-nums">{bucket ? formatSampleValue(bucket.value) : '—'}</Table.Cell>
								{/each}
								<Table.Cell class="text-right font-medium tabular-nums">{formatSampleValue(row.merged.value)}</Table.Cell>
							</Table.Row>
						{/each}
					</Table.Body>
				</Table.Root>
			</div>
		</section>
	{/if}
</div>
