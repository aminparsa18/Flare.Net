<script lang="ts">
	// The "Traces" half of AddPanelDialog.svelte - see AddPanelLogsForm.svelte's header
	// comment for the general shape (dialog-scoped explorer state, minimal filter bar
	// reusing TracesToolbar's own controls rather than the toolbar shell, live TraceList
	// preview).
	import { onMount, onDestroy } from 'svelte';
	import { TracesExplorerState } from '$lib/traces/state.svelte';
	import { tracesExplorerContext } from '$lib/traces/context';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import TraceList from '$lib/components/traces/TraceList.svelte';
	import * as Select from '$lib/components/ui/select';
	import { TIME_RANGE_PRESETS, presetLabel, type TimeRangePreset } from '$lib/logs/time-range';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import * as m from '$lib/paraglide/messages';

	const explorer = tracesExplorerContext.set(new TracesExplorerState());

	// No custom-range calendar - same "only fixed-duration presets make sense here" call
	// TracesToolbar.svelte's own remarks make (TracesExplorerState has no customRange
	// field at all).
	const presets = TIME_RANGE_PRESETS.filter((p) => p.value !== 'custom');

	onMount(() => {
		// Unlike LogsExplorerState (whose VolumeChart preview fetches its own histogram
		// independently of any search run - see AddPanelLogsForm.svelte), TraceList
		// renders `explorer.traces`, which only a `runSearch()` populates - nothing here
		// runs one implicitly the way applySavedViewState does for an existing panel.
		void explorer.runSearch();
		void explorer.loadKnownServices();
	});
	onDestroy(() => explorer.dispose());

	const serviceOptions = $derived(explorer.knownServices.map((s) => ({ value: s, label: s })));

	export function currentState(): unknown {
		return explorer.toSavedViewState();
	}
</script>

<div class="flex flex-wrap items-center gap-2">
	<Select.Root
		type="single"
		value={explorer.filter.timeRangePreset}
		onValueChange={(v) => v && explorer.setTimeRangePreset(v as TimeRangePreset)}
	>
		<Select.Trigger class="w-auto">
			<ClockIcon data-icon="inline-start" />
			{presetLabel(explorer.filter.timeRangePreset)}
		</Select.Trigger>
		<Select.Content>
			{#each presets as preset (preset.value)}
				<Select.Item value={preset.value} label={presetLabel(preset.value)} />
			{/each}
		</Select.Content>
	</Select.Root>
	<PopoverMultiSelect
		label={m.tracesToolbar_serviceLabel()}
		options={serviceOptions}
		selected={explorer.filter.services}
		onChange={(next) => explorer.setServices(next)}
	/>
</div>
<div class="flex h-56 min-h-0 flex-col overflow-hidden rounded-md border">
	<TraceList />
</div>
