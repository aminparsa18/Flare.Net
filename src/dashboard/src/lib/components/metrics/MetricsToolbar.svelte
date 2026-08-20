<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import ViewsMenu from '$lib/components/saved-views/ViewsMenu.svelte';
	import { Switch } from '$lib/components/ui/switch';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import { TIME_RANGE_PRESETS, type TimeRangePreset } from '$lib/logs/time-range';

	const explorer = metricsExplorerContext.get();

	// No live-tail / custom-range calendar here either - same "only fixed-duration
	// presets make sense" call TracesToolbar already made, for the same reason.
	const presets = TIME_RANGE_PRESETS.filter((p) => p.value !== 'custom');

	const serviceOptions = $derived(explorer.knownServices.map((s) => ({ value: s, label: s })));

	const activeLabel = $derived(presets.find((p) => p.value === explorer.filter.timeRangePreset)?.label ?? 'Time range');

	// Bits UI's Select needs a non-empty item value, so a sentinel stands in for "no
	// grouping" and is translated back to null at the call site below.
	const GROUP_BY_NONE = '__none__';
	const groupByLabel = $derived(
		explorer.filter.groupByAttributeKey ? `Group by: ${explorer.filter.groupByAttributeKey}` : 'Group by'
	);
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<Select.Root
		type="single"
		value={explorer.filter.timeRangePreset}
		onValueChange={(v) => v && explorer.setTimeRangePreset(v as TimeRangePreset)}
	>
		<Select.Trigger class="w-auto">
			<ClockIcon data-icon="inline-start" />
			{activeLabel}
		</Select.Trigger>
		<Select.Content>
			{#each presets as preset (preset.value)}
				<Select.Item value={preset.value} label={preset.label} />
			{/each}
		</Select.Content>
	</Select.Root>

	<PopoverMultiSelect
		label="Service"
		options={serviceOptions}
		selected={explorer.filter.services}
		onChange={(next) => explorer.setServices(next)}
	/>

	<!-- Real server-side grouping (collapses series sharing one attribute key's value -
	     see MetricSeriesQueryBuilder's remarks), not a display reshape, so this lives here
	     alongside the other filter-affecting controls rather than in MetricChart.svelte
	     (which only holds pure client-side reshapes like sumMode/histogramMode). Hidden
	     entirely when the selected metric has no discovered attribute keys, same
	     graceful-degradation call as every other picker on this page. -->
	{#if explorer.knownAttributeKeys.length > 0}
		<Select.Root
			type="single"
			value={explorer.filter.groupByAttributeKey ?? GROUP_BY_NONE}
			onValueChange={(v) => v && explorer.setGroupByAttribute(v === GROUP_BY_NONE ? null : v)}
		>
			<Select.Trigger class="w-auto">
				{groupByLabel}
			</Select.Trigger>
			<Select.Content>
				<Select.Item value={GROUP_BY_NONE} label="None" />
				{#each explorer.knownAttributeKeys as key (key.key)}
					<Select.Item value={key.key} label={`${key.key} (${key.distinctValueCount})`} />
				{/each}
			</Select.Content>
		</Select.Root>
	{/if}

	<!-- MetricChart itself is the one that decides whether/how comparison actually
	     renders (unsupported for Histogram's Percentiles view - see its own remarks on
	     compareActive/compareUnavailable), so this switch stays available regardless of
	     the currently-selected metric's type rather than disabling/hiding depending on
	     selection, same "toolbar filter, chart decides what to do with it" split every
	     other filter here already has. A plain `title`, not a rich Tooltip.* - this is
	     one static sentence, not something that needs its own Provider/Root/Trigger
	     wiring (MetricChart's own hover tooltips are for genuinely dynamic content, e.g.
	     the exact compared dates). -->
	<label
		class="flex items-center gap-1.5 text-xs font-medium"
		title="Compares the current time range to the period immediately before it, of the same length (e.g. Last 24 hours vs. the 24 hours before that)."
	>
		<Switch
			checked={explorer.filter.compareEnabled}
			onCheckedChange={(v) => explorer.setCompareEnabled(v)}
			size="sm"
		/>
		Compare with previous period
	</label>

	<ViewsMenu
		pageType="Metrics"
		currentState={() => explorer.toSavedViewState()}
		applyState={(s) => explorer.applySavedViewState(s)}
	/>
</div>
