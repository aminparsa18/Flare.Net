<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import ViewsMenu from '$lib/components/saved-views/ViewsMenu.svelte';
	import TracesViewTabs from '$lib/components/traces/TracesViewTabs.svelte';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import PlayIcon from '@lucide/svelte/icons/play';
	import { traceFunnelContext } from '$lib/trace-funnels/context';
	import { FUNNEL_WINDOW_PRESETS, type FunnelWindowPreset } from '$lib/trace-funnels/state.svelte';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import * as m from '$lib/paraglide/messages';

	const funnel = traceFunnelContext.get();
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<TracesViewTabs activeTab="funnels" />
	<Select.Root type="single" value={funnel.windowPreset} onValueChange={(v) => v && funnel.setWindowPreset(v as FunnelWindowPreset)}>
		<Select.Trigger class="ml-auto w-auto">
			<ClockIcon data-icon="inline-start" />
			{servicesWindowPresetLabel(funnel.windowPreset)}
		</Select.Trigger>
		<Select.Content>
			{#each FUNNEL_WINDOW_PRESETS as preset (preset.value)}
				<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
			{/each}
		</Select.Content>
	</Select.Root>
	<ViewsMenu pageType="Funnels" currentState={() => funnel.currentViewState()} applyState={(state) => funnel.applySavedViewState(state)} />
	<Button size="sm" onclick={() => funnel.run()} disabled={!funnel.runnable || funnel.loading} title={funnel.runnable ? undefined : m.funnelsPage_stepIncomplete()}>
		{#if funnel.loading}
			<Spinner class="size-4" data-icon="inline-start" />
		{:else}
			<PlayIcon data-icon="inline-start" />
		{/if}
		{m.funnelsPage_run()}
	</Button>
</div>
