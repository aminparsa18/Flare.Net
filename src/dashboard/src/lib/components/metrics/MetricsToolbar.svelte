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

	<!-- MetricChart itself is the one that decides whether comparison actually renders
	     (off for Histogram - see its own remarks), so this switch stays available
	     regardless of the currently-selected metric's type rather than disabling/hiding
	     depending on selection, same "toolbar filter, chart decides what to do with it"
	     split every other filter here already has. -->
	<label class="flex items-center gap-1.5 text-xs font-medium">
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
