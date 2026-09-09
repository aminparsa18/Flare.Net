<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import { servicesContext } from '$lib/services/context';
	import { SERVICES_WINDOW_PRESETS, servicesWindowPresetLabel, type ServicesWindowPreset } from '$lib/services/state.svelte';
	import * as m from '$lib/paraglide/messages';

	const services = servicesContext.get();

	// servicesWindowPresetLabel(), not a static `.label` field - same reasoning
	// IngestionToolbar.svelte's own activeLabel gives (time-range.ts's remarks on why a
	// module-scope const can't reflect a per-request/live-switched locale).
	const activeLabel = $derived(servicesWindowPresetLabel(services.windowPreset));
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center justify-between gap-x-6 gap-y-2 border-b px-4 py-2">
	<div>
		<h1 class="text-sm font-semibold">{m.servicesToolbar_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.servicesToolbar_description()}</p>
	</div>
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
