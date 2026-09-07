<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import { ingestionContext } from '$lib/ingestion/context';
	import { INGESTION_WINDOW_PRESETS, ingestionWindowPresetLabel, type IngestionWindowPreset } from '$lib/ingestion/state.svelte';
	import IngestionHealthStatus from './IngestionHealthStatus.svelte';
	import IngestionTopology from './IngestionTopology.svelte';
	import * as m from '$lib/paraglide/messages';

	const ingestion = ingestionContext.get();

	// ingestionWindowPresetLabel(), not a static `.label` field - see time-range.ts's own
	// remarks on why that field was removed (a module-scope const can't reflect a
	// per-request locale).
	const activeLabel = $derived(
		INGESTION_WINDOW_PRESETS.some((p) => p.value === ingestion.windowPreset)
			? ingestionWindowPresetLabel(ingestion.windowPreset)
			: m.ingestionToolbar_windowFallback()
	);
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center justify-between gap-x-6 gap-y-2 border-b px-4 py-2">
	<div>
		<h1 class="text-sm font-semibold">{m.ingestionToolbar_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.ingestionToolbar_description()}</p>
	</div>
	<IngestionHealthStatus />
	<div class="flex items-center gap-2">
		<IngestionTopology />
		<Select.Root
			type="single"
			value={ingestion.windowPreset}
			onValueChange={(v) => v && ingestion.setWindowPreset(v as IngestionWindowPreset)}
		>
			<Select.Trigger class="w-auto">
				<ClockIcon data-icon="inline-start" />
				{activeLabel}
			</Select.Trigger>
			<Select.Content>
				{#each INGESTION_WINDOW_PRESETS as preset (preset.value)}
					<Select.Item value={preset.value} label={ingestionWindowPresetLabel(preset.value)} />
				{/each}
			</Select.Content>
		</Select.Root>
	</div>
</div>