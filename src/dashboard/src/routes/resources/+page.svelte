<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { ResourcesState } from '$lib/resources/state.svelte';
	import { HostStatsState } from '$lib/resources/host-stats.svelte';
	import { resourcesContext } from '$lib/resources/context';
	import ResourceGraph from '$lib/resources/ResourceGraph.svelte';
	import HostOverview from '$lib/resources/HostOverview.svelte';
	import { Empty, EmptyHeader, EmptyMedia, EmptyTitle, EmptyDescription } from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { Switch } from '$lib/components/ui/switch';
	import NetworkIcon from '@lucide/svelte/icons/network';
	import * as m from '$lib/paraglide/messages';

	const resources = resourcesContext.set(new ResourcesState());
	/** Independent stream from `resources` above - see HostOverview.svelte's own remark on why it isn't gated by the topology graph's enablement. */
	const hostStats = new HostStatsState();

	/** Shown by default - see ResourceGraph.svelte's identical remark on its own prop of the same name. */
	let showResourceNodes = $state(true);

	onMount(() => {
		void resources.connect();
		void hostStats.connect();
	});

	onDestroy(() => {
		resources.dispose();
		hostStats.dispose();
	});

	const statusVariant = $derived(
		resources.connectionStatus === 'open' ? 'secondary' : resources.connectionStatus === 'error' ? 'destructive' : 'outline'
	);
</script>

<svelte:head>
	<title>{m.resourcesPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<div class="bg-background sticky top-0 z-10 flex items-center gap-2 border-b px-4 py-2">
		<h1 class="text-sm font-medium">{m.resourcesPage_heading()}</h1>
		<Badge variant={statusVariant} class="ml-1">{resources.connectionStatus}</Badge>
		{#if resources.error}
			<span class="text-destructive text-xs">{resources.error}</span>
		{/if}
		<label for="show-flare-resources" class="ml-auto flex items-center gap-2 text-xs">
			{m.resourcesPage_flareResourcesLabel()}
			<Switch id="show-flare-resources" bind:checked={showResourceNodes} />
		</label>
	</div>
	<HostOverview snapshot={hostStats.snapshot} history={hostStats.history} />
	<div class="min-h-0 flex-1">
		{#if resources.snapshot?.available}
			<ResourceGraph snapshot={resources.snapshot} {showResourceNodes} />
		{:else}
			<Empty>
				<EmptyHeader>
					<EmptyMedia variant="icon">
						<NetworkIcon />
					</EmptyMedia>
					<EmptyTitle>{m.resourcesPage_graphDisabledTitle()}</EmptyTitle>
					<EmptyDescription>
						{resources.snapshot?.unavailableReason ?? m.resourcesPage_graphDisabledDescription()}
					</EmptyDescription>
				</EmptyHeader>
			</Empty>
		{/if}
	</div>
</div>
