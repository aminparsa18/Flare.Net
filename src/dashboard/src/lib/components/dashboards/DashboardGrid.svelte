<script lang="ts">
	// Drag/resize grid host for a dashboard's panels, wrapping gridstack.js (13.x, MIT,
	// framework-agnostic - chosen over a Svelte-native grid lib whose declared peer
	// dependency is Svelte 4, risky under this repo's Svelte 5 runes; see
	// docs-internal/adr/0024-custom-dashboards-phase2-editor.md).
	//
	// Add/remove goes through a Svelte action (`gridItem` below), not imperative
	// grid.addWidget/removeWidget calls scattered elsewhere - the action's mount/destroy
	// hooks piggyback on the keyed {#each}'s own lifecycle, so the *only* thing any other
	// code needs to do is push/filter `panels`. Two independent systems (Svelte's DOM
	// diffing and gridstack's own node bookkeeping) never fight over who owns adding or
	// removing a node - each item's mount tells gridstack "here's a widget", its destroy
	// tells gridstack "that widget's gone", and Svelte does the actual DOM work either way.
	//
	// Position/size is otherwise one-way *out* of gridstack: `panel.layout` seeds each
	// item's initial `gs-x/y/w/h` attributes (gridstack reads those once at
	// makeWidget()-time), and the `change` event - fired once per completed drag/resize,
	// not per pixel - is the only path back into `onLayoutChange`. Dragging `panels` itself
	// back through gridstack mid-interaction isn't attempted; `editing`'s `setStatic` toggle
	// is the one imperative call made outside that add/remove/change loop.
	import { onMount, tick } from 'svelte';
	import { GridStack, type GridStackNode } from 'gridstack';
	import 'gridstack/dist/gridstack.min.css';
	import DashboardPanelCard from './DashboardPanelCard.svelte';
	import type { DashboardPanel } from '$lib/dashboards-api';
	import type { TimeRangePreset } from '$lib/logs/time-range';

	let {
		panels,
		editing,
		timeRangeOverride,
		removingPanelId,
		onLayoutChange,
		onRemove,
		onRename
	}: {
		panels: DashboardPanel[];
		editing: boolean;
		timeRangeOverride: TimeRangePreset | null;
		removingPanelId: string | null;
		onLayoutChange: (next: { id: string; layout: DashboardPanel['layout'] }[]) => void;
		onRemove: (id: string) => void;
		onRename: (id: string, title: string) => void;
	} = $props();

	let container: HTMLDivElement;
	let grid: GridStack | null = null;

	onMount(() => {
		grid = GridStack.init(
			{
				column: 12,
				cellHeight: 70,
				margin: 8,
				float: true,
				staticGrid: !editing,
				handle: '.panel-drag-handle',
				removable: false
			},
			container
		);

		grid?.on('change', (_event, items) => {
			const changed = items
				.filter((n): n is GridStackNode & { id: string } => typeof n.id === 'string')
				.map((n) => ({ id: n.id, layout: { x: n.x ?? 0, y: n.y ?? 0, w: n.w ?? 1, h: n.h ?? 1 } }));
			if (changed.length > 0) onLayoutChange(changed);
		});

		return () => {
			grid?.destroy(false);
			grid = null;
		};
	});

	// Locks/unlocks drag+resize without tearing the grid down - the grid instance and
	// every widget's current position both survive the toggle.
	$effect(() => {
		grid?.setStatic(!editing);
	});

	/** Registers/unregisters one item with the already-initialized grid, driven entirely by its own mount/destroy - see the header comment. */
	function gridItem(node: HTMLElement, panel: DashboardPanel) {
		// The grid may not exist yet on the very first paint (its onMount runs after
		// children have already mounted once) - wait a tick so `grid` is set before the
		// first item tries to register itself.
		void tick().then(() => grid?.makeWidget(node));
		return {
			destroy() {
				grid?.removeWidget(node, false, false);
			}
		};
	}
</script>

<div bind:this={container} class="dashboard-grid grid-stack">
	{#each panels as panel (panel.id)}
		<!-- gridstack's gs-x/y/w/h/id aren't standard HTML attributes svelte-check's
		     HTMLAttributes typing knows about - spreading them (rather than writing them
		     as inline attributes) sidesteps that, same trick used anywhere this codebase
		     needs a non-standard attribute name. -->
		<div
			class="grid-stack-item"
			use:gridItem={panel}
			{...{ 'gs-id': panel.id, 'gs-x': panel.layout.x, 'gs-y': panel.layout.y, 'gs-w': panel.layout.w, 'gs-h': panel.layout.h }}
		>
			<div class="grid-stack-item-content">
				<DashboardPanelCard
					{panel}
					{editing}
					{timeRangeOverride}
					removing={removingPanelId === panel.id}
					onRemove={() => onRemove(panel.id)}
					onRename={(title) => onRename(panel.id, title)}
				/>
			</div>
		</div>
	{/each}
</div>

<style>
	/* Retheme gridstack's own chrome (item shadow/background, resize handle, drag
	   placeholder) onto this app's design tokens instead of its packaged default look -
	   see routes/layout.css for the --border/--muted/--primary definitions this reads. */
	.dashboard-grid :global(.grid-stack-item-content) {
		inset: 4px;
		overflow: hidden;
		border-radius: var(--radius);
	}

	.dashboard-grid :global(.grid-stack-placeholder > .placeholder-content) {
		background: var(--muted);
		border: 1px dashed var(--border);
		border-radius: var(--radius);
	}

	.dashboard-grid :global(.ui-resizable-handle) {
		--gs-resize-handle-color: var(--muted-foreground);
	}
</style>
