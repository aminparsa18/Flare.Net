<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import TracesViewTabs from '$lib/components/traces/TracesViewTabs.svelte';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import { servicesContext } from '$lib/services/context';
	import { SERVICES_WINDOW_PRESETS, servicesWindowPresetLabel, type ServicesWindowPreset } from '$lib/services/state.svelte';

	interface Props {
		activeTab: 'traces' | 'services';
		onTabChange: (tab: 'traces' | 'services') => void;
	}

	let { activeTab, onTabChange }: Props = $props();

	const services = servicesContext.get();

	// servicesWindowPresetLabel(), not a static `.label` field - same reasoning
	// IngestionToolbar.svelte's own activeLabel gives (time-range.ts's remarks on why a
	// module-scope const can't reflect a per-request/live-switched locale). Deliberately
	// its own window control, not TracesToolbar's shared timeRangePreset - the RED
	// rollup is capped at 24h server-side (ServiceOverviewQueryBuilder.MaxWindowMinutes,
	// an unfiltered-by-service GROUP BY has no index to lean on beyond partition
	// pruning), while the trace list's own presets go out to 365d for historical
	// search; unifying the two controls would either silently clamp a "365d" selection
	// down to 24h for this tab, or let the trace list's search itself get capped to a
	// day - neither is right, so they stay independent per-tab controls instead.
	const activeLabel = $derived(servicesWindowPresetLabel(services.windowPreset));
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<TracesViewTabs {activeTab} {onTabChange} />
	<Select.Root
		type="single"
		value={services.windowPreset}
		onValueChange={(v) => v && services.setWindowPreset(v as ServicesWindowPreset)}
	>
		<Select.Trigger class="w-auto">
			<ClockIcon data-icon="inline-start" />
			{activeLabel}
		</Select.Trigger>
		<Select.Content>
			{#each SERVICES_WINDOW_PRESETS as preset (preset.value)}
				<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
			{/each}
		</Select.Content>
	</Select.Root>
</div>
