<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import { ingestionContext } from '$lib/ingestion/context';
	import { INGESTION_WINDOW_PRESETS, type IngestionWindowPreset } from '$lib/ingestion/state.svelte';

	const ingestion = ingestionContext.get();

	const activeLabel = $derived(
		INGESTION_WINDOW_PRESETS.find((p) => p.value === ingestion.windowPreset)?.label ?? 'Window'
	);
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center justify-between gap-2 border-b px-4 py-2">
	<div>
		<h1 class="text-sm font-semibold">Ingestion</h1>
		<p class="text-muted-foreground text-xs">Live OTLP receiver throughput and rejected payloads.</p>
	</div>
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
				<Select.Item value={preset.value} label={preset.label} />
			{/each}
		</Select.Content>
	</Select.Root>
</div>
