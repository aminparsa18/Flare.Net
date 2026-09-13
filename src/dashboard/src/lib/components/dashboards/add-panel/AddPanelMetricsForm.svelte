<script lang="ts">
	// The "Metrics" half of AddPanelDialog.svelte - see AddPanelLogsForm.svelte's header
	// comment for the general shape. Unlike Logs/Traces, this one has no sensible default
	// query (a metrics panel needs a selected metric) - `valid` is a bindable prop the
	// parent disables its submit button on, since MetricsExplorerState.selected can only
	// change after this mounts (MetricPicker's own onSelect), unlike `currentState()`
	// below, which is only ever read once at submit time.
	import { onMount, onDestroy } from 'svelte';
	import { MetricsExplorerState } from '$lib/metrics/state.svelte';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import PopoverSingleSelect from '$lib/components/logs/PopoverSingleSelect.svelte';
	import MetricPicker from '$lib/components/metrics/MetricPicker.svelte';
	import MetricChart from '$lib/components/metrics/MetricChart.svelte';
	import * as Select from '$lib/components/ui/select';
	import { TIME_RANGE_PRESETS, presetLabel, type TimeRangePreset } from '$lib/logs/time-range';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import * as m from '$lib/paraglide/messages';

	let { valid = $bindable(false) }: { valid?: boolean } = $props();

	const explorer = metricsExplorerContext.set(new MetricsExplorerState());
	const presets = TIME_RANGE_PRESETS.filter((p) => p.value !== 'custom');

	onMount(() => {
		void explorer.loadNames();
		void explorer.loadKnownServices();
	});
	onDestroy(() => explorer.dispose());

	$effect(() => {
		valid = explorer.selected != null;
	});

	const serviceOptions = $derived(explorer.knownServices.map((s) => ({ value: s, label: s })));

	const GROUP_BY_NONE = '__none__';
	const groupByLabel = $derived(
		explorer.filter.groupByAttributeKey
			? m.metricsToolbar_groupByWithKey({ key: explorer.filter.groupByAttributeKey })
			: m.metricsToolbar_groupByLabel()
	);
	const groupByOptions = $derived([
		{ value: GROUP_BY_NONE, label: m.metricsToolbar_groupByNone() },
		...explorer.knownAttributeKeys.map((key) => ({ value: key.key, label: `${key.key} (${key.distinctValueCount})` }))
	]);

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
		label={m.metricsToolbar_serviceLabel()}
		options={serviceOptions}
		selected={explorer.filter.services}
		onChange={(next) => explorer.setServices(next)}
	/>
	{#if explorer.knownAttributeKeys.length > 0}
		<PopoverSingleSelect
			label={m.metricsToolbar_groupByLabel()}
			triggerLabel={groupByLabel}
			options={groupByOptions}
			value={explorer.filter.groupByAttributeKey ?? GROUP_BY_NONE}
			onChange={(v) => explorer.setGroupByAttribute(v === GROUP_BY_NONE ? null : v)}
		/>
	{/if}
</div>
<div class="flex h-56 min-h-0 overflow-hidden rounded-md border">
	<MetricPicker width={220} />
	<div class="min-w-0 flex-1 overflow-hidden">
		<MetricChart />
	</div>
</div>
