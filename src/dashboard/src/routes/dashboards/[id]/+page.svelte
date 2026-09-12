<script lang="ts">
	import { page } from '$app/state';
	import { onMount } from 'svelte';
	import { DashboardViewerState } from '$lib/dashboards/viewer.svelte';
	import DashboardPanelCard from '$lib/components/dashboards/DashboardPanelCard.svelte';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import LayoutDashboardIcon from '@lucide/svelte/icons/layout-dashboard';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import * as m from '$lib/paraglide/messages';

	const viewer = new DashboardViewerState();

	onMount(() => {
		void viewer.load(page.params.id!);
	});
</script>

<svelte:head>
	<title>{viewer.dashboard ? `Flare — ${viewer.dashboard.name}` : m.dashboardsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<div class="flex items-center gap-2 border-b px-4 py-3">
		<Button variant="ghost" size="icon-sm" href="/dashboards" title={m.dashboardViewer_back()}>
			<ArrowLeftIcon />
		</Button>
		<div class="min-w-0">
			<h1 class="truncate text-sm font-semibold">{viewer.dashboard?.name ?? ''}</h1>
			{#if viewer.dashboard?.description}
				<p class="text-muted-foreground truncate text-xs">{viewer.dashboard.description}</p>
			{/if}
		</div>
	</div>

	{#if viewer.loading}
		<div class="flex flex-1 items-center justify-center">
			<Spinner />
		</div>
	{:else if viewer.error}
		<div class="flex flex-1 items-center justify-center">
			<p class="text-destructive text-sm">{viewer.error}</p>
		</div>
	{:else if !viewer.dashboard}
		<div class="flex flex-1 items-center justify-center">
			<p class="text-muted-foreground text-sm">{m.dashboardViewer_notFound()}</p>
		</div>
	{:else if viewer.dashboard.layout.panels.length === 0}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Media>
					<LayoutDashboardIcon class="text-muted-foreground size-8" />
				</Empty.Media>
				<Empty.Title>{m.dashboardViewer_emptyTitle()}</Empty.Title>
				<Empty.Description>{m.dashboardViewer_emptyDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<div class="min-h-0 flex-1 space-y-4 overflow-y-auto p-4">
			{#each viewer.dashboard.layout.panels as panel (panel.id)}
				<DashboardPanelCard {panel} removing={viewer.removingPanelId === panel.id} onRemove={() => viewer.removePanel(panel.id)} />
			{/each}
		</div>
	{/if}
</div>
