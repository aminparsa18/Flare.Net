<script lang="ts">
	// Flame graph over the already-loaded trace (signoz#7889 prior art): span width =
	// duration, stacked by depth, colored by service or any field (TraceColorLegend) - "where did the time go" for a trace
	// too large to read row by row in the waterfall. Same hand-rolled absolutely-positioned
	// divs as TraceWaterfall, no charting library. Selection is TraceDetailState's
	// selectedSpanId, so clicking a bar opens the same SpanDetailSheet the waterfall does.
	import type { SpanDto } from '$lib/traces-api';
	import { formatDurationNano } from '$lib/traces/duration';
	import { traceDetailContext } from '$lib/traces/trace-context';
	import { computeFlameLayout, type FlameBar } from '$lib/traces/flame-graph';
	import { statusLabel } from '$lib/traces/status';
	import { formatTimeOfDay } from '$lib/time/format';
	import TraceColorLegend from './TraceColorLegend.svelte';
	import { Button } from '$lib/components/ui/button';
	import ZoomOutIcon from '@lucide/svelte/icons/zoom-out';
	import * as m from '$lib/paraglide/messages';
	import { onDestroy, onMount, tick, untrack } from 'svelte';

	const detail = traceDetailContext.get();

	const ROW_HEIGHT = 20;
	const ROW_GAP = 2;

	const layout = $derived(computeFlameLayout(detail.trace?.spans ?? []));

	// Visible time window, in ms since trace start. null = the whole trace. Double-clicking
	// a bar zooms to that span; per-view UI state only, same as the waterfall's collapse set.
	let zoom = $state<{ start: number; end: number } | null>(null);
	const viewStart = $derived(zoom?.start ?? 0);
	const viewEnd = $derived(zoom?.end ?? layout.totalMs);
	// Floored so a zoom onto a zero-duration span still has a non-zero window to divide by.
	const viewMs = $derived(Math.max(viewEnd - viewStart, 0.001));

	const visibleBars = $derived(layout.bars.filter((b) => b.endMs >= viewStart && b.startMs <= viewEnd));

	const ticks = $derived([0, 0.25, 0.5, 0.75, 1].map((fraction) => viewStart + fraction * viewMs));

	// Clipped to the window, so a wide parent's label stays on screen while zoomed into
	// one of its children instead of starting somewhere off to the left.
	function barStyle(bar: FlameBar): string {
		const left = Math.max(bar.startMs, viewStart);
		const right = Math.min(bar.endMs, viewEnd);
		const leftPct = ((left - viewStart) / viewMs) * 100;
		const widthPct = ((right - left) / viewMs) * 100;
		const color = detail.colorOf(bar.span);
		const fill = color
			? `background: color-mix(in oklab, ${color} 35%, var(--background)); border-color: ${color};`
			: '';
		return `left: ${leftPct}%; width: ${widthPct}%; top: ${bar.level * (ROW_HEIGHT + ROW_GAP)}px; height: ${ROW_HEIGHT}px; ${fill}`;
	}

	// A click opens SpanDetailSheet, which is modal - its overlay would swallow the second
	// click of a double-click. So a single click waits out the double-click window before
	// selecting, and a second click cancels it and zooms instead.
	const DOUBLE_CLICK_MS = 250;
	let pendingSelect: ReturnType<typeof setTimeout> | undefined;
	function onBarClick(e: MouseEvent, bar: FlameBar) {
		clearTimeout(pendingSelect);
		if (e.detail >= 2) zoomTo(bar);
		else pendingSelect = setTimeout(() => (detail.selectedSpanId = bar.span.spanId), DOUBLE_CLICK_MS);
	}
	onDestroy(() => clearTimeout(pendingSelect));

	function zoomTo(bar: FlameBar) {
		// Zooming onto the whole trace is just "reset".
		zoom = bar.startMs <= 0 && bar.endMs >= layout.totalMs ? null : { start: bar.startMs, end: bar.endMs };
	}

	// Hover card: one floating element following the pointer rather than a Tooltip.Root
	// per bar - a large trace is exactly the case this view exists for, and thousands of
	// tooltip instances is what the waterfall already pays for.
	let hovered = $state<{ span: SpanDto; startMs: number; x: number; y: number } | null>(null);

	// Lands a `?span=` deep link (or a span selected in the waterfall before switching
	// tabs) already scrolled to its bar, same as the waterfall does for its row.
	let scroller = $state<HTMLElement>();
	async function scrollToBar(spanId: string, behavior: ScrollBehavior) {
		await tick();
		scroller?.querySelector(`[data-flame-bar="${spanId}"]`)?.scrollIntoView({ block: 'center', inline: 'nearest', behavior });
	}
	onMount(() => {
		if (detail.selectedSpanId) void scrollToBar(detail.selectedSpanId, 'instant');
	});

	// Span search: scroll the current match into view as the user steps through them,
	// dropping the zoom first if it sits outside the zoomed window (it has no bar there).
	$effect(() => {
		const id = detail.focusedMatchId;
		if (!id) return;
		untrack(() => {
			const bar = layout.bars.find((b) => b.span.spanId === id);
			if (bar && (bar.endMs < viewStart || bar.startMs > viewEnd)) zoom = null;
			void scrollToBar(id, 'smooth');
		});
	});
</script>

<div class="flex min-h-0 flex-1 flex-col">
	<div class="flex shrink-0 flex-wrap items-center gap-x-3 gap-y-1 border-b px-3 py-1.5 text-xs">
		<TraceColorLegend />
		<span class="text-muted-foreground ml-auto">{m.traceFlameGraph_hint()}</span>
		{#if zoom}
			<Button variant="outline" size="sm" class="h-6 text-xs" onclick={() => (zoom = null)}>
				<ZoomOutIcon data-icon="inline-start" />
				{m.traceFlameGraph_resetZoom()}
			</Button>
		{/if}
	</div>

	<div
		class="bg-muted/30 text-muted-foreground relative shrink-0 border-b text-xs font-medium"
		style="height: 28px;"
	>
		<!-- Same inset + last-tick -translate-x-full trick as the waterfall's axis. -->
		<div class="absolute inset-y-0 right-3 left-3">
			{#each ticks as t, i (i)}
				<span
					class="absolute top-1/2 -translate-y-1/2 {i === ticks.length - 1 ? '-translate-x-full' : ''}"
					style="left: {((t - viewStart) / viewMs) * 100}%;"
				>
					{formatDurationNano(t * 1_000_000)}
				</span>
			{/each}
		</div>
	</div>

	<div class="min-h-0 flex-1 overflow-auto px-3 py-2" bind:this={scroller}>
		<div
			class="relative overflow-hidden"
			style="height: {layout.levels * (ROW_HEIGHT + ROW_GAP)}px;"
			role="presentation"
			onmouseleave={() => (hovered = null)}
		>
			{#each visibleBars as bar (bar.span.spanId)}
				{@const isError = bar.span.statusCode === 'STATUS_CODE_ERROR'}
				<button
					type="button"
					data-flame-bar={bar.span.spanId}
					class="text-foreground absolute min-w-px truncate rounded-sm border px-1 text-left text-[11px] leading-[18px] hover:brightness-110 focus-visible:outline-none
						{detail.colorOf(bar.span) ? '' : 'bg-muted border-muted-foreground/40'}
						{isError ? 'ring-destructive ring-2 ring-inset' : ''}
						{detail.selectedSpanId === bar.span.spanId ? 'outline-foreground z-10 outline-2' : ''}
						{detail.focusedMatchId === bar.span.spanId ? 'outline-primary z-10 outline-2 outline-offset-1' : ''}
						{detail.spanSearchActive && !detail.spanSearchMatchSet.has(bar.span.spanId) ? 'opacity-25' : ''}"
					style={barStyle(bar)}
					aria-label="{bar.span.name || '—'} · {bar.span.serviceName || '—'} · {formatDurationNano(bar.span.durationNano)}"
					onclick={(e) => onBarClick(e, bar)}
					onmousemove={(e) => (hovered = { span: bar.span, startMs: bar.startMs, x: e.clientX, y: e.clientY })}
				>
					{bar.span.name || '—'}
				</button>
			{/each}
		</div>
	</div>
</div>

{#if hovered}
	<!-- Fixed to the viewport, offset from the pointer so it never sits under it, and
	     flipped to the pointer's other side near the right/bottom edge. -->
	<div
		class="bg-foreground text-background pointer-events-none fixed z-50 flex max-w-80 flex-col gap-0.5 rounded-md px-3 py-1.5 text-xs
			{hovered.x > window.innerWidth - 340 ? '-translate-x-full' : ''} {hovered.y > window.innerHeight - 160 ? '-translate-y-full' : ''}"
		style="left: {hovered.x > window.innerWidth - 340 ? hovered.x - 12 : hovered.x + 12}px; top: {hovered.y > window.innerHeight - 160
			? hovered.y - 12
			: hovered.y + 12}px;"
	>
		<span class="font-medium break-words">{hovered.span.name || '—'}</span>
		<span class="opacity-70">{hovered.span.serviceName || '—'}</span>
		<span>{m.traceWaterfall_hoverDuration({ duration: formatDurationNano(hovered.span.durationNano) })}</span>
		<span>{m.traceWaterfall_hoverOffset({ offset: formatDurationNano(hovered.startMs * 1_000_000) })}</span>
		<span class="opacity-70 tabular-nums">{formatTimeOfDay(hovered.span.startTime, 'ms')}</span>
		{#if hovered.span.statusCode === 'STATUS_CODE_ERROR'}
			<span class="font-medium">{statusLabel(hovered.span.statusCode)}</span>
		{/if}
	</div>
{/if}
