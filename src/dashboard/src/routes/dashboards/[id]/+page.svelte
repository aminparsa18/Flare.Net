<script lang="ts">
	import { page } from '$app/state';
	import { onMount, onDestroy } from 'svelte';
	import { DashboardViewerState } from '$lib/dashboards/viewer.svelte';
	import { REFRESH_INTERVALS, refreshIntervalLabel, type RefreshInterval } from '$lib/dashboards/refresh-intervals';
	import { getChromeVisibilityContext } from '$lib/chrome/context.svelte';
	import DashboardGrid from '$lib/components/dashboards/DashboardGrid.svelte';
	import AddPanelDialog from '$lib/components/dashboards/AddPanelDialog.svelte';
	import * as Empty from '$lib/components/ui/empty';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import { TIME_RANGE_PRESETS, presetLabel, type TimeRangePreset } from '$lib/logs/time-range';
	import LayoutDashboardIcon from '@lucide/svelte/icons/layout-dashboard';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import HomeIcon from '@lucide/svelte/icons/home';
	import Maximize2Icon from '@lucide/svelte/icons/maximize-2';
	import Minimize2Icon from '@lucide/svelte/icons/minimize-2';
	import * as m from '$lib/paraglide/messages';

	const viewer = new DashboardViewerState();
	let addPanelOpen = $state(false);

	// Full-screen/TV mode: hides AppNav (via the layout-provided chrome signal - see
	// $lib/chrome/context.svelte.ts) and asks the browser's Fullscreen API to fill the screen with
	// just this route's own root element, for wall-mounted displays. `isFullscreen` mirrors
	// `document.fullscreenElement` rather than being toggled directly, so it stays correct
	// however fullscreen ends - our own button, the browser's native Escape handling, or the
	// user hitting F11 - since all three fire the same `fullscreenchange` event.
	const chrome = getChromeVisibilityContext();
	let rootEl = $state<HTMLDivElement>();
	let isFullscreen = $state(false);
	// Safari's un-prefixed detection lags; requestFullscreen()/exitFullscreen() are still
	// called through the standard API below and simply no-op (button hidden) where absent.
	const fullscreenSupported = typeof document !== 'undefined' && document.fullscreenEnabled;

	function handleFullscreenChange(): void {
		isFullscreen = document.fullscreenElement === rootEl;
		chrome.hidden = isFullscreen;
	}

	function toggleFullscreen(): void {
		if (isFullscreen) {
			void document.exitFullscreen();
		} else {
			void rootEl?.requestFullscreen();
		}
	}

	onMount(() => {
		void viewer.load(page.params.id!);
		document.addEventListener('fullscreenchange', handleFullscreenChange);
	});

	onDestroy(() => {
		viewer.dispose();
		document.removeEventListener('fullscreenchange', handleFullscreenChange);
		// Leaving the route (not just toggling off) must always give the nav back, even if
		// the browser is still mid-fullscreen (e.g. navigating away via a keyboard shortcut).
		if (document.fullscreenElement === rootEl) void document.exitFullscreen();
		chrome.hidden = false;
	});

	// Same "fixed-duration presets only" set Traces'/Metrics' own toolbars offer - see
	// DashboardViewerState.timeRangeOverride's own remarks on why this can't cover an
	// absolute custom range (TracesExplorerState has no customRange concept at all).
	const OVERRIDE_OFF = '__off__';
	const overridePresets = TIME_RANGE_PRESETS.filter((p) => p.value !== 'custom');
	const overrideLabel = $derived(
		viewer.timeRangeOverride ? presetLabel(viewer.timeRangeOverride) : m.dashboardViewer_timeRangeOverrideOff()
	);

	function handleOverrideChange(value: string): void {
		viewer.setTimeRangeOverride(value === OVERRIDE_OFF ? null : (value as TimeRangePreset));
	}

	function handleRefreshIntervalChange(value: string): void {
		viewer.setRefreshInterval(value as RefreshInterval);
	}
</script>

<svelte:head>
	<title>{viewer.dashboard ? `Flare — ${viewer.dashboard.name}` : m.dashboardsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col" bind:this={rootEl}>
	<div class="flex flex-wrap items-center gap-2 border-b px-4 py-3">
		<Button variant="ghost" size="icon-sm" href="/dashboards" title={m.dashboardViewer_back()}>
			<ArrowLeftIcon />
		</Button>
		<div class="min-w-0">
			<h1 class="truncate text-sm font-semibold">{viewer.dashboard?.name ?? ''}</h1>
			{#if viewer.dashboard?.description}
				<p class="text-muted-foreground truncate text-xs">{viewer.dashboard.description}</p>
			{/if}
		</div>

		{#if viewer.dashboard}
			<div class="ml-auto flex items-center gap-2">
				<Button
					variant={viewer.isHome ? 'secondary' : 'ghost'}
					size="icon-sm"
					title={viewer.isHome ? m.dashboardViewer_unsetHome() : m.dashboardViewer_setHome()}
					onclick={() => viewer.toggleHome()}
				>
					<HomeIcon />
				</Button>

				<Select.Root type="single" value={viewer.refreshInterval} onValueChange={(v) => v && handleRefreshIntervalChange(v)}>
					<Select.Trigger class="w-auto" title={m.dashboardViewer_refreshIntervalTitle()}>
						<RefreshCwIcon data-icon="inline-start" />
						{refreshIntervalLabel(viewer.refreshInterval)}
					</Select.Trigger>
					<Select.Content>
						{#each REFRESH_INTERVALS as interval (interval.value)}
							<Select.Item value={interval.value} label={refreshIntervalLabel(interval.value)} />
						{/each}
					</Select.Content>
				</Select.Root>

				<Select.Root type="single" value={viewer.timeRangeOverride ?? OVERRIDE_OFF} onValueChange={(v) => v && handleOverrideChange(v)}>
					<Select.Trigger class="w-auto" title={m.dashboardViewer_timeRangeOverrideTitle()}>
						<ClockIcon data-icon="inline-start" />
						{overrideLabel}
					</Select.Trigger>
					<Select.Content>
						<Select.Item value={OVERRIDE_OFF} label={m.dashboardViewer_timeRangeOverrideOff()} />
						{#each overridePresets as preset (preset.value)}
							<Select.Item value={preset.value} label={presetLabel(preset.value)} />
						{/each}
					</Select.Content>
				</Select.Root>

				{#if viewer.editing}
					<Button variant="outline" size="sm" onclick={() => (addPanelOpen = true)}>
						<PlusIcon data-icon="inline-start" />
						{m.dashboardViewer_addPanel()}
					</Button>
				{/if}

				<Button
					variant={viewer.editing ? 'secondary' : 'outline'}
					size="sm"
					onclick={() => viewer.setEditing(!viewer.editing)}
				>
					<PencilIcon data-icon="inline-start" />
					{viewer.editing ? m.dashboardViewer_doneEditing() : m.dashboardViewer_edit()}
				</Button>

				{#if fullscreenSupported}
					<Button
						variant="ghost"
						size="icon-sm"
						title={isFullscreen ? m.dashboardViewer_exitFullscreen() : m.dashboardViewer_fullscreen()}
						onclick={toggleFullscreen}
					>
						{#if isFullscreen}
							<Minimize2Icon />
						{:else}
							<Maximize2Icon />
						{/if}
					</Button>
				{/if}
			</div>
		{/if}
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
			<Empty.Content>
				<Button size="sm" onclick={() => (addPanelOpen = true)}>
					<PlusIcon data-icon="inline-start" />
					{m.dashboardViewer_addPanel()}
				</Button>
			</Empty.Content>
		</Empty.Root>
	{:else}
		<div class="min-h-0 flex-1 overflow-y-auto p-4">
			<DashboardGrid
				panels={viewer.dashboard.layout.panels}
				editing={viewer.editing}
				timeRangeOverride={viewer.timeRangeOverride}
				refreshToken={viewer.refreshToken}
				removingPanelId={viewer.removingPanelId}
				onLayoutChange={(changes) => viewer.updateLayout(changes)}
				onRemove={(id) => viewer.removePanel(id)}
				onRename={(id, title) => viewer.renamePanel(id, title)}
			/>
		</div>
	{/if}
</div>

<AddPanelDialog bind:open={addPanelOpen} existingPanels={viewer.dashboard?.layout.panels ?? []} onAdd={(panel) => viewer.addPanel(panel)} />
