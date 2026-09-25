<script lang="ts">
	import * as Select from '$lib/components/ui/select';
	import { Input } from '$lib/components/ui/input';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import { hostsContext } from '$lib/hosts/context';
	import { HOSTS_WINDOW_PRESETS, type HostsWindowPreset } from '$lib/hosts/state.svelte';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import * as m from '$lib/paraglide/messages';

	const hosts = hostsContext.get();

	// Sentinel for "all" - bits-ui's Select can't carry an empty-string item value.
	const ALL_OS = '__all__';
</script>

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<h1 class="text-sm font-medium">{m.hostsPage_heading()}</h1>
	<Input
		type="search"
		class="ml-2 h-8 w-56"
		placeholder={m.hostsPage_searchPlaceholder()}
		aria-label={m.hostsPage_searchPlaceholder()}
		value={hosts.search}
		oninput={(e) => hosts.setSearch(e.currentTarget.value)}
	/>
	<Select.Root type="single" value={hosts.osType || ALL_OS} onValueChange={(v) => hosts.setOsType(v === ALL_OS ? '' : v)}>
		<Select.Trigger class="w-auto" aria-label={m.hostsPage_osFilterLabel()}>
			{hosts.osType || m.hostsPage_allOsTypes()}
		</Select.Trigger>
		<Select.Content>
			<Select.Item value={ALL_OS} label={m.hostsPage_allOsTypes()} />
			{#each hosts.knownOsTypes as osType (osType)}
				<Select.Item value={osType} label={osType} />
			{/each}
		</Select.Content>
	</Select.Root>
	<Select.Root type="single" value={hosts.windowPreset} onValueChange={(v) => v && hosts.setWindowPreset(v as HostsWindowPreset)}>
		<Select.Trigger class="ml-auto w-auto">
			<ClockIcon data-icon="inline-start" />
			{servicesWindowPresetLabel(hosts.windowPreset)}
		</Select.Trigger>
		<Select.Content>
			{#each HOSTS_WINDOW_PRESETS as preset (preset.value)}
				<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
			{/each}
		</Select.Content>
	</Select.Root>
</div>
