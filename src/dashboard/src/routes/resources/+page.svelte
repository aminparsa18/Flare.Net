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

	const resources = resourcesContext.set(new ResourcesState());
	/** Independent stream from `resources` above - see HostOverview.svelte's own remark on why it isn't gated by the Docker graph's enablement. */
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
	<title>Flare — Resources</title>
</svelte:head>

<div class="flex h-full flex-col">
	<div class="bg-background sticky top-0 z-10 flex items-center gap-2 border-b px-4 py-2">
		<h1 class="text-sm font-medium">Resources</h1>
		<Badge variant={statusVariant} class="ml-1">{resources.connectionStatus}</Badge>
		{#if resources.error}
			<span class="text-destructive text-xs">{resources.error}</span>
		{/if}
		<label for="show-flare-resources" class="ml-auto flex items-center gap-2 text-xs">
			Flare resources
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
					<EmptyTitle>Resources graph not enabled</EmptyTitle>
					<EmptyDescription>
						{resources.snapshot?.unavailableReason ??
							'Waiting for Flare.Api - the Docker resource graph is off by default and needs an explicit opt-in.'}
					</EmptyDescription>
				</EmptyHeader>
			</Empty>
		{/if}
	</div>
</div>
