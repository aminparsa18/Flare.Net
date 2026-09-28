<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import { externalApisContext } from '$lib/external-apis/context';
	import { EXTERNAL_APIS_WINDOW_PRESETS, type ExternalApisWindowPreset } from '$lib/external-apis/state.svelte';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import * as m from '$lib/paraglide/messages';

	const externalApis = externalApisContext.get();

	// Sentinel for "all" - bits-ui's Select can't carry an empty-string item value.
	const ALL = '__all__';
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<h1 class="text-sm font-medium">{m.externalApisPage_heading()}</h1>
	<Select.Root type="single" value={externalApis.service || ALL} onValueChange={(v) => externalApis.setService(v === ALL ? '' : v)}>
		<Select.Trigger class="ml-2 w-auto" aria-label={m.externalApisPage_serviceFilterLabel()}>
			{externalApis.service || m.externalApisPage_allServices()}
		</Select.Trigger>
		<Select.Content>
			<Select.Item value={ALL} label={m.externalApisPage_allServices()} />
			{#each externalApis.services as service (service)}
				<Select.Item value={service} label={service} />
			{/each}
		</Select.Content>
	</Select.Root>
	<Select.Root type="single" value={externalApis.windowPreset} onValueChange={(v) => v && externalApis.setWindowPreset(v as ExternalApisWindowPreset)}>
		<Select.Trigger class="ml-auto w-auto">
			<ClockIcon data-icon="inline-start" />
			{servicesWindowPresetLabel(externalApis.windowPreset)}
		</Select.Trigger>
		<Select.Content>
			{#each EXTERNAL_APIS_WINDOW_PRESETS as preset (preset.value)}
				<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
			{/each}
		</Select.Content>
	</Select.Root>
	<Button variant="outline" size="sm" onclick={() => externalApis.load()} disabled={externalApis.loading}>
		{#if externalApis.loading}
			<Spinner class="size-4" data-icon="inline-start" />
		{:else}
			<RefreshCwIcon data-icon="inline-start" />
		{/if}
		{m.externalApisPage_refresh()}
	</Button>
</div>
