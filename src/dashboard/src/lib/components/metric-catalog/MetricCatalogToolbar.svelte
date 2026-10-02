<script lang="ts">
	import { withBase } from '$lib/paths';
	import * as Select from '$lib/components/ui/select';
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import { metricCatalogContext } from '$lib/metric-catalog/context';
	import { METRIC_CATALOG_WINDOW_PRESETS, type MetricCatalogWindowPreset } from '$lib/metric-catalog/state.svelte';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import { formatCount } from '$lib/ingestion/format';
	import * as m from '$lib/paraglide/messages';

	const catalog = metricCatalogContext.get();

	const totalSeries = $derived((catalog.metrics ?? []).reduce((sum, metric) => sum + metric.seriesCount, 0));
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<Button variant="ghost" size="sm" href={withBase('/metrics')}>
		<ArrowLeftIcon data-icon="inline-start" />
		{m.metricCatalog_backToExplorer()}
	</Button>
	<h1 class="text-sm font-medium">{m.metricCatalog_heading()}</h1>
	<Input
		type="search"
		class="ml-2 h-8 w-64"
		placeholder={m.metricCatalog_searchPlaceholder()}
		aria-label={m.metricCatalog_searchPlaceholder()}
		value={catalog.search}
		oninput={(e) => catalog.setSearch(e.currentTarget.value)}
	/>
	{#if catalog.metrics && catalog.metrics.length > 0}
		<span class="text-muted-foreground text-xs tabular-nums">
			{m.metricCatalog_summary({ count: catalog.metrics.length, series: formatCount(totalSeries) })}
		</span>
	{/if}
	<div class="ml-auto flex items-center gap-2">
		<Button variant="outline" size="sm" onclick={() => catalog.refresh()} disabled={catalog.loading}>
			<RefreshCwIcon data-icon="inline-start" class={catalog.loading ? 'animate-spin' : ''} />
			{m.metricCatalog_refresh()}
		</Button>
		<Select.Root
			type="single"
			value={catalog.windowPreset}
			onValueChange={(v) => v && catalog.setWindowPreset(v as MetricCatalogWindowPreset)}
		>
			<Select.Trigger class="w-auto">
				<ClockIcon data-icon="inline-start" />
				{servicesWindowPresetLabel(catalog.windowPreset)}
			</Select.Trigger>
			<Select.Content>
				{#each METRIC_CATALOG_WINDOW_PRESETS as preset (preset.value)}
					<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
				{/each}
			</Select.Content>
		</Select.Root>
	</div>
</div>
