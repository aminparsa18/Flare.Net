<script lang="ts">
	// The one genuinely new visualization in this roadmap slice - no existing component
	// in this dashboard renders a parent-child timeline. Hand-rolled with plain
	// absolutely-positioned divs (not SVG, not @xyflow/svelte - the latter is in
	// package.json but unused anywhere in this app and isn't a good fit for a strictly
	// time-axis layout), the same "hand-roll it, no charting library" precedent
	// VolumeChart.svelte already set for this dashboard.
	import type { SpanDto } from '$lib/traces-api';
	import { formatDurationNano } from '$lib/traces/duration';
	import { traceDetailContext } from '$lib/traces/trace-context';
	import { computeCriticalPath } from '$lib/traces/critical-path';
	import ZapIcon from '@lucide/svelte/icons/zap';

	const detail = traceDetailContext.get();

	interface WaterfallRow {
		span: SpanDto;
		depth: number;
	}

	/**
	 * Flattens the trace's spans into parent-before-children render order with a depth
	 * per row, via a straightforward tree walk. A span whose `parentSpanId` doesn't
	 * point at another span in this trace (absent, or - defensively - a parent that
	 * hasn't landed/was dropped) is treated as a root rather than silently omitted, so
	 * every fetched span always renders somewhere. `visited` guards against a
	 * malformed/cyclic parent reference looping forever - real OTLP data never does
	 * this, but nothing upstream validates it either.
	 */
	const rows = $derived.by((): WaterfallRow[] => {
		const spans = detail.trace?.spans ?? [];
		if (spans.length === 0) return [];

		const spanIds = new Set(spans.map((s) => s.spanId));
		const byParent = new Map<string, SpanDto[]>();
		for (const span of spans) {
			const parentKey = span.parentSpanId && spanIds.has(span.parentSpanId) ? span.parentSpanId : '';
			const siblings = byParent.get(parentKey);
			if (siblings) siblings.push(span);
			else byParent.set(parentKey, [span]);
		}
		for (const siblings of byParent.values()) {
			siblings.sort((a, b) => a.startTime.localeCompare(b.startTime));
		}

		const result: WaterfallRow[] = [];
		const visited = new Set<string>();
		function visit(span: SpanDto, depth: number) {
			if (visited.has(span.spanId)) return;
			visited.add(span.spanId);
			result.push({ span, depth });
			for (const child of byParent.get(span.spanId) ?? []) visit(child, depth + 1);
		}
		for (const root of byParent.get('') ?? []) visit(root, 0);
		return result;
	});

	const criticalPath = $derived(computeCriticalPath(detail.trace?.spans ?? []));
	const criticalSpanIds = $derived(new Set(criticalPath?.contributionMs.keys() ?? []));
	// Floored at 1ms, same reasoning as `totalMs` below - avoids a divide-by-zero for
	// the percentage callout on a (degenerate) zero-duration root span.
	const rootDurationMs = $derived(
		criticalPath ? Math.max(1, new Date(criticalPath.root.endTime).getTime() - new Date(criticalPath.root.startTime).getTime()) : 1
	);

	const traceStartMs = $derived(Math.min(...(detail.trace?.spans.map((s) => new Date(s.startTime).getTime()) ?? [0])));
	const traceEndMs = $derived(Math.max(...(detail.trace?.spans.map((s) => new Date(s.endTime).getTime()) ?? [0])));
	// Floored at 1ms so a trace with a single zero-duration span doesn't divide by zero.
	const totalMs = $derived(Math.max(1, traceEndMs - traceStartMs));

	const ticks = $derived([0, 0.25, 0.5, 0.75, 1].map((fraction) => fraction * totalMs));

	function barStyle(span: SpanDto): string {
		const startOffsetMs = new Date(span.startTime).getTime() - traceStartMs;
		const durationMs = Math.max(new Date(span.endTime).getTime() - new Date(span.startTime).getTime(), 0);
		const leftPct = (startOffsetMs / totalMs) * 100;
		// Minimum visible width so a near-zero-duration span (common for cheap internal
		// operations) still renders a clickable/visible sliver instead of vanishing.
		const widthPct = Math.max((durationMs / totalMs) * 100, 0.5);
		return `left: ${leftPct}%; width: ${widthPct}%;`;
	}

	function barColorClass(statusCode: string): string {
		switch (statusCode) {
			case 'STATUS_CODE_ERROR':
				return 'bg-destructive';
			case 'STATUS_CODE_OK':
				return 'bg-primary';
			default:
				return 'bg-muted-foreground/50';
		}
	}
</script>

<div class="flex min-h-0 flex-1 flex-col overflow-auto">
	<!-- Two-column grid (label | timeline), shared across the axis row and every span
	     row via the same CSS custom property trick TraceList/LogTable already use, so
	     the axis's tick marks stay pixel-aligned with every bar beneath them. -->
	<div class="flex min-h-0 flex-1 flex-col" style:--waterfall-label-width="280px">
		<!-- Callout + axis header stick together as one unit (rather than each being
		     independently `sticky top-0`, which would make the second one overlap the
		     first once both are pinned) - same trick as `--waterfall-label-width`, just
		     for stacking order instead of column alignment. -->
		<div class="sticky top-0 z-10 flex shrink-0 flex-col">
			<!-- Critical-path callout: which spans actually determined when this trace
			     finished, as opposed to work that ran concurrently and simply lost the
			     race. Only worth a row when there's more than one span to distinguish - a
			     single-span trace is trivially "100% critical path" and saying so is noise. -->
			{#if criticalPath && criticalPath.topContributor && (detail.trace?.spans.length ?? 0) > 1}
				<div class="bg-warning/10 text-warning flex items-center gap-1.5 border-b px-3 py-1.5 text-xs">
					<ZapIcon class="size-3.5 shrink-0" />
					<span class="font-medium">Critical path</span>
					<span class="text-warning/70">·</span>
					<span>{criticalSpanIds.size} of {detail.trace?.spans.length} spans</span>
					<span class="text-warning/70">·</span>
					<span>
						<span class="font-medium">{criticalPath.topContributor.span.name || '—'}</span>
						{' '}accounts for {Math.round((criticalPath.topContributor.ms / rootDurationMs) * 100)}% of the trace
						({formatDurationNano(criticalPath.topContributor.ms * 1_000_000)})
					</span>
				</div>
			{/if}
			<div
				class="bg-muted/30 text-muted-foreground grid items-center border-b text-xs font-medium"
				style="grid-template-columns: var(--waterfall-label-width) 1fr; height: 28px;"
			>
				<span class="px-3">Span</span>
				<!-- pr-3 + the last tick's own -translate-x-full keep the "total duration" label
				     flush with, not overflowing past, the container's right edge - a left-anchored
				     0% tick needs no such adjustment, so only the last one gets it. -->
				<div class="relative h-full pr-3">
					{#each ticks as tick, i (tick)}
						<span
							class="absolute top-1/2 -translate-y-1/2 {i === ticks.length - 1 ? '-translate-x-full' : ''}"
							style="left: {(tick / totalMs) * 100}%;"
						>
							{formatDurationNano(tick * 1_000_000)}
						</span>
					{/each}
				</div>
			</div>
		</div>

		{#each rows as { span, depth } (span.spanId)}
			<button
				type="button"
				class="hover:bg-muted/50 focus-visible:bg-muted/50 grid w-full items-center border-b text-left focus-visible:outline-none"
				class:bg-muted={detail.selectedSpanId === span.spanId}
				style="grid-template-columns: var(--waterfall-label-width) 1fr; height: 32px;"
				onclick={() => (detail.selectedSpanId = span.spanId)}
			>
				<!-- min-w-0 on the flex row + flex-1/min-w-0 on the name span is required for the
				     name to actually shrink-to-ellipsis instead of being crowded out entirely -
				     flex items default to min-width:auto, so without this a long serviceName
				     (real OTel SDK default is "unknown_service:<process name>", easily 30+
				     chars) pushed the name span's rendered width to ~0. The service name gets a
				     capped max-width instead of shrink-0 so it truncates too rather than
				     dominating the row. -->
				<span class="flex min-w-0 items-center gap-1 px-3 text-sm" style="padding-left: {12 + depth * 16}px;">
					{#if criticalSpanIds.has(span.spanId)}
						<ZapIcon class="text-warning size-3 shrink-0" />
					{/if}
					<span class="min-w-0 flex-1 truncate">{span.name || '—'}</span>
					<span class="text-muted-foreground max-w-[40%] shrink truncate text-xs">· {span.serviceName || '—'}</span>
				</span>
				<div class="relative h-5">
					<!-- Critical-path spans render at full color/opacity; everything else fades
					     back so the handful of spans that actually determined the trace's end
					     time stand out from the ones that just ran alongside them (see
					     $lib/traces/critical-path.ts). -->
					<div
						class="{barColorClass(span.statusCode)} absolute top-0 h-full min-w-[2px] rounded-sm {criticalSpanIds.has(
							span.spanId
						)
							? 'ring-warning opacity-100 ring-2'
							: 'opacity-40'}"
						style={barStyle(span)}
						title="{span.name} — {formatDurationNano(span.durationNano)}"
					></div>
				</div>
			</button>
		{/each}
	</div>
</div>
