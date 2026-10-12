<script lang="ts">
	// Icicle flame graph of one merged profile call tree (src/lib/profiles/layout.ts).
	// Click a frame to zoom onto its subtree; same hand-rolled absolutely-positioned divs
	// as TraceFlameGraph, no charting library.
	import type { FlameGraphResponse } from '$lib/profiles-api';
	import { layoutProfile, formatProfileValue, frameColor, type ProfileCell } from '$lib/profiles/layout';
	import { Button } from '$lib/components/ui/button';
	import ZoomOutIcon from '@lucide/svelte/icons/zoom-out';
	import * as m from '$lib/paraglide/messages';

	let { graph }: { graph: FlameGraphResponse } = $props();

	const ROW_HEIGHT = 20;
	const ROW_GAP = 2;

	let focusPath = $state('');
	// A new response is a new tree; a stale path would point into the old one.
	$effect(() => {
		void graph;
		focusPath = '';
	});

	const layout = $derived(layoutProfile(graph.root, focusPath));
	const rootTotal = $derived(Math.max(layout.cells[0]?.node.total ?? 1, 1));

	let hovered = $state<{ cell: ProfileCell; x: number; y: number } | null>(null);

	function cellStyle(cell: ProfileCell): string {
		return `left: ${cell.x * 100}%; width: ${cell.width * 100}%; top: ${cell.depth * (ROW_HEIGHT + ROW_GAP)}px; height: ${ROW_HEIGHT}px; background: color-mix(in oklab, ${frameColor(cell.node.name)} 55%, var(--background));`;
	}
</script>

<div class="flex min-h-0 flex-1 flex-col">
	<div class="flex shrink-0 flex-wrap items-center gap-x-3 gap-y-1 border-b px-3 py-1.5 text-xs">
		<span class="text-muted-foreground">
			{m.profilesPage_summary({ total: formatProfileValue(graph.root.total, graph.sampleUnit), stacks: graph.stackCount })}
		</span>
		{#if graph.truncated}
			<span class="text-amber-600">{m.profilesPage_truncated()}</span>
		{/if}
		<span class="text-muted-foreground ml-auto">{m.profilesPage_hint()}</span>
		{#if focusPath}
			<Button variant="outline" size="sm" class="h-6 text-xs" onclick={() => (focusPath = '')}>
				<ZoomOutIcon data-icon="inline-start" />
				{m.profilesPage_resetZoom()}
			</Button>
		{/if}
	</div>

	<div class="min-h-0 flex-1 overflow-auto px-3 py-2">
		<div
			class="relative overflow-hidden"
			style="height: {layout.levels * (ROW_HEIGHT + ROW_GAP)}px;"
			role="presentation"
			onmouseleave={() => (hovered = null)}
		>
			{#each layout.cells as cell (cell.path)}
				<button
					type="button"
					class="text-foreground absolute min-w-px truncate rounded-sm border border-black/20 px-1 text-left font-mono text-[11px] leading-[18px] hover:brightness-110 focus-visible:outline-none"
					style={cellStyle(cell)}
					aria-label="{cell.node.name} · {formatProfileValue(cell.node.total, graph.sampleUnit)}"
					onclick={() => (focusPath = cell.path)}
					onmousemove={(e) => (hovered = { cell, x: e.clientX, y: e.clientY })}
				>
					{cell.node.name}
				</button>
			{/each}
		</div>
	</div>
</div>

{#if hovered}
	<div
		class="bg-foreground text-background pointer-events-none fixed z-50 flex max-w-96 flex-col gap-0.5 rounded-md px-3 py-1.5 text-xs
			{hovered.x > window.innerWidth - 400 ? '-translate-x-full' : ''} {hovered.y > window.innerHeight - 140 ? '-translate-y-full' : ''}"
		style="left: {hovered.x > window.innerWidth - 400 ? hovered.x - 12 : hovered.x + 12}px; top: {hovered.y > window.innerHeight - 140
			? hovered.y - 12
			: hovered.y + 12}px;"
	>
		<span class="font-mono font-medium break-words">{hovered.cell.node.name}</span>
		<span>
			{m.profilesPage_hoverTotal({
				value: formatProfileValue(hovered.cell.node.total, graph.sampleUnit),
				percent: ((hovered.cell.node.total / rootTotal) * 100).toFixed(1)
			})}
		</span>
		<span class="opacity-70">{m.profilesPage_hoverSelf({ value: formatProfileValue(hovered.cell.node.self, graph.sampleUnit) })}</span>
	</div>
{/if}
