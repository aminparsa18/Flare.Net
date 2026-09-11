<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import ViewsMenu from '$lib/components/saved-views/ViewsMenu.svelte';
	import { Switch } from '$lib/components/ui/switch';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import { TIME_RANGE_PRESETS, presetLabel, formatCustomRangeLabel, type TimeRangePreset } from '$lib/logs/time-range';
	import * as m from '$lib/paraglide/messages';

	const explorer = metricsExplorerContext.get();

	// This Select itself still offers only fixed-duration presets, no manual custom-range
	// calendar - same "only fixed-duration presets make sense to *pick*" call TracesToolbar
	// already made (comparison mode's previous-period math wants a nameable duration for
	// its label - see time-range.ts's PREVIOUS_PERIOD_LABELS). Unlike before, the filter
	// itself *can* land on 'custom' now - MetricChart's drag-to-zoom sets it via
	// MetricsExplorerState.setCustomRange - so activeLabel below still has to render that
	// state even though selecting it here isn't offered.
	const presets = TIME_RANGE_PRESETS.filter((p) => p.value !== 'custom');

	const serviceOptions = $derived(explorer.knownServices.map((s) => ({ value: s, label: s })));

	// presetLabel(), not a static `.label` field - see time-range.ts's own remarks on why
	// that field was removed (a module-scope const can't reflect a per-request locale).
	// 'custom' (reachable only via drag-to-zoom, see `presets` above) gets the same
	// full-precision range label TimeRangePicker's own Trigger shows for Logs, not the
	// generic "Time range" fallback this used to show for it - it's a genuine result of a
	// user action, not an unreachable/defensive case.
	const activeLabel = $derived(
		explorer.filter.timeRangePreset === 'custom'
			? formatCustomRangeLabel(explorer.filter.customRange)
			: presetLabel(explorer.filter.timeRangePreset)
	);

	// Bits UI's Select needs a non-empty item value, so a sentinel stands in for "no
	// grouping" and is translated back to null at the call site below.
	const GROUP_BY_NONE = '__none__';
	const groupByLabel = $derived(
		explorer.filter.groupByAttributeKey
			? m.metricsToolbar_groupByWithKey({ key: explorer.filter.groupByAttributeKey })
			: m.metricsToolbar_groupByLabel()
	);

	// Fixed option set, not a free-text/numeric input - same "closed, sane set of
	// choices" call PopoverMultiSelect/the other Select-backed toolbar pickers already
	// make, and it keeps every value within MetricSeriesQueryBuilder's MaxTopN clamp by
	// construction. Bits UI's Select needs string item values, so values round-trip
	// through Number()/String() at the call sites below.
	const TOP_N_OPTIONS = [5, 10, 20, 50, 100];
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
				<Select.Item value={preset.value} label={presetLabel(preset.value)} />
			{/each}
		</Select.Content>
	</Select.Root>

	<PopoverMultiSelect
		label={m.metricsToolbar_serviceLabel()}
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
				<Select.Item value={GROUP_BY_NONE} label={m.metricsToolbar_groupByNone()} />
				{#each explorer.knownAttributeKeys as key (key.key)}
					<Select.Item value={key.key} label={`${key.key} (${key.distinctValueCount})`} />
				{/each}
			</Select.Content>
		</Select.Root>
	{/if}

	<!-- The series cap (MetricQueryRequest.topN / MetricSeriesQueryBuilder's remarks)
	     applies unconditionally server-side regardless of this control - see there for
	     why. Surfaced here only once grouping is actually on, though: that's the "top 10
	     hosts by error rate" case the roadmap called out, and an ungrouped metric rarely
	     has enough distinct series for the default cap to matter, so there's nothing
	     useful for this picker to do until a group-by key narrows the series down to a
	     rankable set. -->
	{#if explorer.filter.groupByAttributeKey}
		<Select.Root
			type="single"
			value={String(explorer.filter.topN)}
			onValueChange={(v) => v && explorer.setTopN(Number(v))}
		>
			<Select.Trigger class="w-auto" title={m.metricsToolbar_topNTitle()}>
				{m.metricsToolbar_topNWithValue({ n: explorer.filter.topN })}
			</Select.Trigger>
			<Select.Content>
				{#each TOP_N_OPTIONS as n (n)}
					<Select.Item value={String(n)} label={String(n)} />
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
	<label class="flex items-center gap-1.5 text-xs font-medium" title={m.metricsToolbar_compareTitle()}>
		<Switch
			checked={explorer.filter.compareEnabled}
			onCheckedChange={(v) => explorer.setCompareEnabled(v)}
			size="sm"
		/>
		{m.metricsToolbar_compareLabel()}
	</label>

	<!-- Re-runs the chart's current query on an interval while on - see
	     MetricsExplorerState.autoRefreshEnabled's own remarks. A plain `title`, same
	     "one static sentence, not a rich Tooltip.*" call the compare switch above already
	     makes. -->
	<label class="flex items-center gap-1.5 text-xs font-medium" title={m.metricsToolbar_autoRefreshTitle()}>
		<Switch
			checked={explorer.autoRefreshEnabled}
			onCheckedChange={(v) => explorer.setAutoRefreshEnabled(v)}
			size="sm"
		/>
		<RefreshCwIcon class="size-3.5" />
		{m.metricsToolbar_autoRefreshLabel()}
	</label>

	<ViewsMenu
		pageType="Metrics"
		currentState={() => explorer.toSavedViewState()}
		applyState={(s) => explorer.applySavedViewState(s)}
	/>
</div>
