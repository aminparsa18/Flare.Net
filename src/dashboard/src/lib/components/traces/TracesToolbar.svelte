<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import ViewsMenu from '$lib/components/saved-views/ViewsMenu.svelte';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import { tracesExplorerContext } from '$lib/traces/context';
	import { TIME_RANGE_PRESETS, presetLabel, type TimeRangePreset } from '$lib/logs/time-range';
	import * as m from '$lib/paraglide/messages';

	const explorer = tracesExplorerContext.get();

	// No live-tail / custom-range calendar for traces (see state.svelte.ts's remarks) -
	// only the fixed-duration presets make sense here, so 'custom' is filtered out
	// rather than reusing TimeRangePicker.svelte (which is tightly coupled to
	// LogsExplorerState's live/custom-range fields).
	const presets = TIME_RANGE_PRESETS.filter((p) => p.value !== 'custom');

	const serviceOptions = $derived(explorer.knownServices.map((s) => ({ value: s, label: s })));

	// presetLabel(), not a static `.label` field - see time-range.ts's own remarks on why
	// that field was removed (a module-scope const can't reflect a per-request locale).
	const activeLabel = $derived(
		presets.some((p) => p.value === explorer.filter.timeRangePreset)
			? presetLabel(explorer.filter.timeRangePreset)
			: m.timeRange_label()
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
				<Select.Item value={preset.value} label={presetLabel(preset.value)} />
			{/each}
		</Select.Content>
	</Select.Root>

	<PopoverMultiSelect
		label="Service"
		options={serviceOptions}
		selected={explorer.filter.services}
		onChange={(next) => explorer.setServices(next)}
	/>
	<ViewsMenu pageType="Traces" currentState={() => explorer.toSavedViewState()} applyState={(s) => explorer.applySavedViewState(s)} />
</div>
