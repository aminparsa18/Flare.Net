<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import { llmContext } from '$lib/llm/context';
	import { LLM_WINDOW_PRESETS, type LlmWindowPreset } from '$lib/llm/state.svelte';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import * as m from '$lib/paraglide/messages';

	const llm = llmContext.get();

	// Sentinel for "all" - bits-ui's Select can't carry an empty-string item value.
	const ALL = '__all__';
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<h1 class="text-sm font-medium">{m.llmPage_heading()}</h1>
	<Select.Root type="single" value={llm.service || ALL} onValueChange={(v) => llm.setService(v === ALL ? '' : v)}>
		<Select.Trigger class="ml-2 w-auto" aria-label={m.llmPage_serviceFilterLabel()}>
			{llm.service || m.llmPage_allServices()}
		</Select.Trigger>
		<Select.Content>
			<Select.Item value={ALL} label={m.llmPage_allServices()} />
			{#each llm.services as service (service)}
				<Select.Item value={service} label={service} />
			{/each}
		</Select.Content>
	</Select.Root>
	<Select.Root type="single" value={llm.windowPreset} onValueChange={(v) => v && llm.setWindowPreset(v as LlmWindowPreset)}>
		<Select.Trigger class="ml-auto w-auto">
			<ClockIcon data-icon="inline-start" />
			{servicesWindowPresetLabel(llm.windowPreset)}
		</Select.Trigger>
		<Select.Content>
			{#each LLM_WINDOW_PRESETS as preset (preset.value)}
				<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
			{/each}
		</Select.Content>
	</Select.Root>
	<Button variant="outline" size="sm" onclick={() => llm.load()} disabled={llm.loading}>
		{#if llm.loading}
			<Spinner class="size-4" data-icon="inline-start" />
		{:else}
			<RefreshCwIcon data-icon="inline-start" />
		{/if}
		{m.llmPage_refresh()}
	</Button>
</div>
