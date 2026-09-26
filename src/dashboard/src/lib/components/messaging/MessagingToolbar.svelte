<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import { messagingContext } from '$lib/messaging/context';
	import { MESSAGING_WINDOW_PRESETS, type MessagingWindowPreset } from '$lib/messaging/state.svelte';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import * as m from '$lib/paraglide/messages';

	const messaging = messagingContext.get();

	// Sentinel for "all" - bits-ui's Select can't carry an empty-string item value.
	const ALL = '__all__';
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<h1 class="text-sm font-medium">{m.messagingPage_heading()}</h1>
	<Select.Root type="single" value={messaging.system || ALL} onValueChange={(v) => messaging.setSystem(v === ALL ? '' : v)}>
		<Select.Trigger class="ml-2 w-auto" aria-label={m.messagingPage_systemFilterLabel()}>
			{messaging.system || m.messagingPage_allSystems()}
		</Select.Trigger>
		<Select.Content>
			<Select.Item value={ALL} label={m.messagingPage_allSystems()} />
			{#each messaging.systems as system (system)}
				<Select.Item value={system} label={system} />
			{/each}
		</Select.Content>
	</Select.Root>
	<Select.Root type="single" value={messaging.service || ALL} onValueChange={(v) => messaging.setService(v === ALL ? '' : v)}>
		<Select.Trigger class="w-auto" aria-label={m.messagingPage_serviceFilterLabel()}>
			{messaging.service || m.messagingPage_allServices()}
		</Select.Trigger>
		<Select.Content>
			<Select.Item value={ALL} label={m.messagingPage_allServices()} />
			{#each messaging.services as service (service)}
				<Select.Item value={service} label={service} />
			{/each}
		</Select.Content>
	</Select.Root>
	<Select.Root type="single" value={messaging.windowPreset} onValueChange={(v) => v && messaging.setWindowPreset(v as MessagingWindowPreset)}>
		<Select.Trigger class="ml-auto w-auto">
			<ClockIcon data-icon="inline-start" />
			{servicesWindowPresetLabel(messaging.windowPreset)}
		</Select.Trigger>
		<Select.Content>
			{#each MESSAGING_WINDOW_PRESETS as preset (preset.value)}
				<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
			{/each}
		</Select.Content>
	</Select.Root>
	<Button variant="outline" size="sm" onclick={() => messaging.load()} disabled={messaging.loading}>
		{#if messaging.loading}
			<Spinner class="size-4" data-icon="inline-start" />
		{:else}
			<RefreshCwIcon data-icon="inline-start" />
		{/if}
		{m.messagingPage_refresh()}
	</Button>
</div>
