<script lang="ts">
	// Single-value ("stat") visualization: one big number for the whole query - the
	// per-bucket total across every series, collapsed by the panel's reducer (see
	// `totalsByBucket`'s remarks for why totals rather than the first series). The first
	// matching threshold rule colors the number, which is what thresholds are most often for
	// on a stat panel ("red above 500 ms").
	import { matchThreshold, thresholdColorValue, type PanelThreshold } from '$lib/dashboards/thresholds';
	import { formatValue, type PanelReducer } from '$lib/dashboards/visualization';
	import { reducerLabel } from './labels';
	import * as m from '$lib/paraglide/messages';

	let {
		value,
		unit,
		reducer,
		seriesCount,
		thresholds = []
	}: {
		value: number;
		unit: string | null;
		reducer: PanelReducer;
		seriesCount: number;
		thresholds?: PanelThreshold[];
	} = $props();

	const match = $derived(matchThreshold(thresholds, value));
</script>

<div class="@container flex min-h-0 flex-1 flex-col items-center justify-center gap-1 p-2 text-center">
	<span
		class="max-w-full truncate text-3xl font-semibold tabular-nums @xs:text-5xl"
		style={match ? `color: ${thresholdColorValue(match.color)};` : undefined}
		title={String(value)}
	>
		{formatValue(value, unit)}
	</span>
	<span class="text-muted-foreground text-xs">
		{reducerLabel(reducer)}{#if seriesCount > 1}{' · '}{m.panelVisualization_valueAcrossSeries({ count: seriesCount })}{/if}
	</span>
</div>
