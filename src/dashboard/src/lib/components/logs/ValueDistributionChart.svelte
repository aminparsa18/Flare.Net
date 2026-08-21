<script lang="ts">
	// Seq calls this the per-event scatter/density chart: pick a numeric property and see
	// its values plotted over time, color-coded by how many events land on the same
	// (time, value) cell. Flare's logs have no first-class numeric field to default to
	// (see Planning.md's "no duration/p95 in v1" rationale on the Patterns feature - the
	// same reasoning applies here) - LogAttributes is a Map(String, String), so any numeric
	// value is just whatever attribute key a given app happens to emit (duration_ms,
	// latency, bytes, ...). So this picks a key from whatever's actually numeric in scope
	// (getLogAttributeKeys) rather than assuming one.
	//
	// Structurally mirrors VolumeChart.svelte: its own Accordion section, its own
	// localStorage collapsed-state key, the same debounced-refetch-on-filter-change
	// $effect (skipped while collapsed), the same hand-rolled SVG (no charting library -
	// see VolumeChart's own remarks on why). Defaults to *collapsed* though (VolumeChart
	// defaults open) - this is a supplementary view most deployments won't have a numeric
	// attribute for yet, so it shouldn't claim vertical space above the log table until
	// opened once.
	import { browser } from '$app/environment';
	import { mode } from 'mode-watcher';
	import {
		getLogAttributeKeys,
		getLogValueDistribution,
		type LogAttributeKeyInfo,
		type LogValueDistributionPoint
	} from '$lib/api';
	import { pickBucketWidthSeconds } from '$lib/logs/bucket-width';
	import { resolveTimeRange } from '$lib/logs/time-range';
	import { logsExplorerContext } from '$lib/logs/context';
	import * as Accordion from '$lib/components/ui/accordion';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import * as Tooltip from '$lib/components/ui/tooltip';

	const explorer = logsExplorerContext.get();

	const LIVE_TRAILING_WINDOW_MS = 60 * 60 * 1000; // matches VolumeChart's own live-mode window, for the same "consistent with the unfiltered-search default" reasoning
	const SAMPLE_SIZE = 4000; // matches LogValueDistributionRequest's own server-side default - kept explicit here so a future change to one doesn't silently change the other

	const ITEM = 'value-distribution';
	const COLLAPSE_STORAGE_KEY = 'flare.logs.valueDistributionCollapsed';

	function loadStoredValue(): string {
		if (!browser) return ''; // collapsed by default - see this file's own header remarks
		try {
			return localStorage.getItem(COLLAPSE_STORAGE_KEY) === 'false' ? ITEM : '';
		} catch {
			return ''; // storage disabled (e.g. private browsing) - fall back to collapsed
		}
	}

	let accordionValue = $state(loadStoredValue());

	$effect(() => {
		const value = accordionValue;
		if (!browser) return;
		try {
			localStorage.setItem(COLLAPSE_STORAGE_KEY, String(value !== ITEM));
		} catch {
			// Non-critical - the next reload just falls back to collapsed instead.
		}
	});

	const collapsed = $derived(accordionValue !== ITEM);
	const isDark = $derived(mode.current === 'dark'); // mode.current is undefined during SSR (mode-watcher's isBrowser guard) - falls back to the light-mode ramp direction until hydration corrects it, same as LogsToolbar's own theme-icon guard

	let keys = $state<LogAttributeKeyInfo[]>([]);
	let selectedKey = $state<string | null>(null);
	let points = $state<LogValueDistributionPoint[]>([]);
	let fetchError = $state<string | null>(null);
	let hoverCell = $state<{ timeIndex: number; valueIndex: number } | null>(null);

	let rangeFrom = $state<string | null>(null);
	let rangeTo = $state<string | null>(null);
	let bucketWidthSeconds = $state(1);

	let logScale = $state(false);

	function currentRange(): { from: string; to: string } {
		if (explorer.live) {
			const to = new Date();
			return { from: new Date(to.getTime() - LIVE_TRAILING_WINDOW_MS).toISOString(), to: to.toISOString() };
		}
		const range = resolveTimeRange(explorer.filter.timeRangePreset, explorer.filter.customRange ?? undefined);
		if (range) return range;
		// Only reachable if 'custom' is selected but no range has been picked yet
		// (resolveTimeRange only returns null for that case - see its own remarks).
		const to = new Date();
		return { from: new Date(to.getTime() - LIVE_TRAILING_WINDOW_MS).toISOString(), to: to.toISOString() };
	}

	// Two separate calls, not one combined fetch - so reselecting the attribute picker
	// doesn't re-run key discovery, and a filter change doesn't discard the user's picker
	// choice unless that key genuinely isn't numeric in the new scope any more.
	async function refreshKeys() {
		const range = currentRange();
		try {
			const res = await getLogAttributeKeys({ filter: explorer.buildFilter(range) });
			keys = res.keys;
			if (!selectedKey || !keys.some((k) => k.key === selectedKey)) {
				selectedKey = keys[0]?.key ?? null;
			}
		} catch (err) {
			fetchError = err instanceof Error ? err.message : String(err);
		}
	}

	async function refreshPoints() {
		if (!selectedKey) {
			points = [];
			return;
		}
		const range = currentRange();
		const rangeSeconds = (new Date(range.to).getTime() - new Date(range.from).getTime()) / 1000;
		try {
			const res = await getLogValueDistribution({
				filter: explorer.buildFilter(range),
				attributeKey: selectedKey,
				sampleSize: SAMPLE_SIZE
			});
			points = res.points;
			rangeFrom = range.from;
			rangeTo = range.to;
			bucketWidthSeconds = pickBucketWidthSeconds(rangeSeconds);
			fetchError = null;
		} catch (err) {
			fetchError = err instanceof Error ? err.message : String(err);
		}
	}

	// Key discovery - depends on the base filter, not `selectedKey` (see refreshKeys'
	// own remarks). Same collapsed short-circuit as VolumeChart's own effect.
	$effect(() => {
		if (collapsed) return;
		void explorer.filter.timeRangePreset;
		void explorer.filter.customRange;
		void explorer.filter.services;
		void explorer.filter.severityNumbers;
		void explorer.filter.search;
		void explorer.live;

		const timer = setTimeout(refreshKeys, 300);
		return () => clearTimeout(timer);
	});

	// Point sampling - additionally depends on `selectedKey`, so switching the picker
	// refetches without waiting on/re-running refreshKeys.
	$effect(() => {
		if (collapsed) return;
		void explorer.filter.timeRangePreset;
		void explorer.filter.customRange;
		void explorer.filter.services;
		void explorer.filter.severityNumbers;
		void explorer.filter.search;
		void explorer.live;
		void selectedKey;

		const timer = setTimeout(refreshPoints, 300);
		return () => clearTimeout(timer);
	});

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 140;
	const BASELINE_Y = CHART_HEIGHT - 2;
	const PEAK_Y = 3;
	const Y_BINS = 24;
	const CELL_GAP = 1.5; // surface-color gap between adjacent cells, both axes - see the dataviz skill's "surface gap" spacer

	// The sample's own min/max for the chosen key - not a fixed/assumed scale, since two
	// different attribute keys (or the same key filtered to a different window) can have
	// wildly different ranges. Values <= 0 are excluded under log scale (Math.log is
	// undefined for them, same reason Seq's own log toggle only applies to positive
	// metrics) - never excluded under linear scale.
	const valueRange = $derived.by((): { min: number; max: number } | null => {
		let min = Infinity;
		let max = -Infinity;
		for (const p of points) {
			if (logScale && p.value <= 0) continue;
			if (p.value < min) min = p.value;
			if (p.value > max) max = p.value;
		}
		if (min === Infinity) return null;
		return min === max ? { min: min - 1, max: max + 1 } : { min, max };
	});

	const bucketCount = $derived(
		rangeFrom && rangeTo
			? Math.max(1, Math.round((new Date(rangeTo).getTime() - new Date(rangeFrom).getTime()) / 1000 / bucketWidthSeconds))
			: 0
	);

	function valueBinIndex(value: number, range: { min: number; max: number }): number | null {
		if (logScale && value <= 0) return null;
		const lo = logScale ? Math.log(range.min) : range.min;
		const hi = logScale ? Math.log(range.max) : range.max;
		const x = logScale ? Math.log(value) : value;
		const t = hi === lo ? 0 : (x - lo) / (hi - lo);
		return Math.min(Y_BINS - 1, Math.max(0, Math.floor(t * Y_BINS)));
	}

	// grid[timeIndex][valueIndex] = point count in that cell.
	const grid = $derived.by((): number[][] => {
		if (!valueRange || bucketCount === 0 || !rangeFrom) return [];
		const cells: number[][] = Array.from({ length: bucketCount }, () => new Array(Y_BINS).fill(0) as number[]);
		const fromMs = new Date(rangeFrom).getTime();
		for (const p of points) {
			const valueIndex = valueBinIndex(p.value, valueRange);
			if (valueIndex === null) continue;
			const timeIndex = Math.min(
				bucketCount - 1,
				Math.max(0, Math.floor((new Date(p.timestamp).getTime() - fromMs) / (bucketWidthSeconds * 1000)))
			);
			cells[timeIndex][valueIndex]++;
		}
		return cells;
	});

	const maxCellCount = $derived(Math.max(1, ...grid.flat()));
	const totalPoints = $derived(points.length);

	// One hue, light->dark (the dataviz skill's default sequential ramp - "Sequential
	// hue: blue"). VolumeChart's own bars use --primary, which this app's theme (see
	// routes/layout.css) defines as chroma-0 (plain gray/white) - so there's no hue to
	// clash with here, this chart is simply the first colored element on the page.
	const SEQUENTIAL_BLUE = ['#cde2fb', '#9ec5f4', '#6da7ec', '#3987e5', '#256abf', '#184f95', '#0d366b'];

	/**
	 * "Flips anchor in dark" (dataviz skill, sequential row): on a light surface the
	 * lightest step means "near zero" and recedes toward the white background, the
	 * darkest step stands out. A dark surface is the opposite - the darkest step is what
	 * recedes, so max density has to be the *lightest* step to still stand out. Same
	 * shape Seq's own dark-mode legend uses (0 = dark purple, max = bright green).
	 */
	function densityColor(count: number, max: number): string | null {
		if (count === 0) return null;
		const t = Math.min(1, count / max);
		const steps = SEQUENTIAL_BLUE;
		const index = isDark
			? Math.round((1 - t) * (steps.length - 1))
			: Math.round(t * (steps.length - 1));
		return steps[index];
	}

	const colWidth = $derived(CHART_WIDTH / Math.max(1, bucketCount));
	const rowHeight = $derived((BASELINE_Y - PEAK_Y) / Y_BINS);

	function cellRect(timeIndex: number, valueIndex: number) {
		const x = timeIndex * colWidth + CELL_GAP / 2;
		const width = Math.max(0.5, colWidth - CELL_GAP);
		// valueIndex 0 is the lowest value - stacks bottom-up, same reading direction as a
		// y-axis (and as VolumeChart's own bars).
		const y = BASELINE_Y - (valueIndex + 1) * rowHeight + CELL_GAP / 2;
		const height = Math.max(0.5, rowHeight - CELL_GAP);
		return { x, y, width, height };
	}

	function bucketIndexAt(svg: SVGSVGElement, clientX: number): number {
		const rect = svg.getBoundingClientRect();
		const fraction = (clientX - rect.left) / rect.width;
		return Math.min(bucketCount - 1, Math.max(0, Math.floor(fraction * bucketCount)));
	}

	function valueIndexAt(svg: SVGSVGElement, clientY: number): number {
		const rect = svg.getBoundingClientRect();
		const fraction = (clientY - rect.top) / rect.height;
		const fromTop = Math.min(1, Math.max(0, fraction));
		return Math.min(Y_BINS - 1, Math.max(0, Y_BINS - 1 - Math.floor(fromTop * Y_BINS)));
	}

	function handlePointerMove(e: PointerEvent) {
		if (bucketCount === 0) return;
		const svg = e.currentTarget as SVGSVGElement;
		hoverCell = { timeIndex: bucketIndexAt(svg, e.clientX), valueIndex: valueIndexAt(svg, e.clientY) };
	}

	// Same click-to-filter parity as VolumeChart: filters the log table to the clicked
	// column's time window, ignoring which value row was clicked - LogFilter/
	// AttributeFilter only support exact-match attribute filtering today, not ranges, so
	// there's no "filter to this value bucket" to wire up yet.
	function handleClick(e: MouseEvent) {
		if (bucketCount === 0 || !rangeFrom) return;
		const timeIndex = bucketIndexAt(e.currentTarget as SVGSVGElement, e.clientX);
		const from = new Date(new Date(rangeFrom).getTime() + timeIndex * bucketWidthSeconds * 1000);
		const to = new Date(from.getTime() + bucketWidthSeconds * 1000);
		explorer.focusBucketRange({ from, to });
	}

	function formatAxisTime(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false, hour: '2-digit', minute: '2-digit' });
	}

	const compactNumber = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 });

	function formatValue(v: number): string {
		return compactNumber.format(v);
	}

	function bucketTimeLabel(timeIndex: number): string {
		if (!rangeFrom) return '';
		const from = new Date(new Date(rangeFrom).getTime() + timeIndex * bucketWidthSeconds * 1000);
		const to = new Date(from.getTime() + bucketWidthSeconds * 1000);
		return `${from.toLocaleString(undefined, { hour12: false, month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })} – ${to.toLocaleTimeString(undefined, { hour12: false, hour: '2-digit', minute: '2-digit' })}`;
	}

	function bucketValueLabel(valueIndex: number): string {
		if (!valueRange) return '';
		const lo = logScale ? Math.log(valueRange.min) : valueRange.min;
		const hi = logScale ? Math.log(valueRange.max) : valueRange.max;
		const toValue = (t: number) => (logScale ? Math.exp(lo + t * (hi - lo)) : lo + t * (hi - lo));
		const low = toValue(valueIndex / Y_BINS);
		const high = toValue((valueIndex + 1) / Y_BINS);
		return `${formatValue(low)} – ${formatValue(high)}`;
	}
</script>

<Accordion.Root type="single" bind:value={accordionValue} class="w-full flex-col rounded-none border-0 border-b">
	<Accordion.Item value={ITEM} class="border-0 data-open:bg-transparent">
		<div class="flex items-center justify-between gap-2 px-4 py-3 text-xs">
			<Accordion.Trigger
				class="text-muted-foreground hover:text-foreground group/accordion-trigger relative flex w-auto flex-none items-center justify-start gap-1 border-none p-0 text-left text-xs font-normal hover:no-underline **:data-[slot=accordion-trigger-icon]:ml-0 **:data-[slot=accordion-trigger-icon]:size-3.5"
			>
				Value distribution
			</Accordion.Trigger>
			{#if !collapsed}
				{#if keys.length > 0}
					<Select.Root type="single" value={selectedKey ?? undefined} onValueChange={(v) => v && (selectedKey = v)}>
						<Select.Trigger class="h-6 w-auto text-xs">
							{selectedKey ?? 'Select attribute'}
						</Select.Trigger>
						<Select.Content>
							{#each keys as key (key.key)}
								<Select.Item value={key.key} label={key.key} />
							{/each}
						</Select.Content>
					</Select.Root>
				{/if}
				<div class="flex-1"></div>
				{#if selectedKey && points.length > 0}
					<span class="text-muted-foreground tabular-nums">{totalPoints} sampled</span>
					<div class="flex items-center gap-1">
						<span class="text-muted-foreground tabular-nums">0</span>
						<div
							class="h-2.5 w-12 rounded-sm"
							style="background: linear-gradient(to right, {isDark
								? SEQUENTIAL_BLUE[SEQUENTIAL_BLUE.length - 1]
								: SEQUENTIAL_BLUE[0]}, {isDark ? SEQUENTIAL_BLUE[0] : SEQUENTIAL_BLUE[SEQUENTIAL_BLUE.length - 1]});"
							title="Events per cell: 0 (recedes) to {maxCellCount} (brightest)"
						></div>
						<span class="text-muted-foreground tabular-nums">{maxCellCount}</span>
					</div>
					<Button
						variant={logScale ? 'default' : 'outline'}
						size="sm"
						class="h-6 px-1.5 font-mono text-[0.65rem]"
						title={logScale ? 'Switch to linear scale' : 'Switch to logarithmic scale'}
						onclick={() => (logScale = !logScale)}
					>
						ln
					</Button>
				{/if}
			{/if}
		</div>
		<Accordion.Content class="px-4 pb-3">
			{#if fetchError}
				<p class="text-destructive text-xs">Value distribution: {fetchError}</p>
			{:else if keys.length === 0}
				<div class="text-muted-foreground flex h-[100px] items-center justify-center text-xs">
					No numeric log attributes in this range
				</div>
			{:else if points.length === 0}
				<div class="text-muted-foreground flex h-[100px] items-center justify-center text-xs">No data</div>
			{:else}
				<div class="grid grid-cols-[3.5rem_1fr] gap-x-2">
					<div class="text-muted-foreground flex h-[140px] flex-col justify-between py-0.5 text-right text-[10px] tabular-nums">
						<span>{valueRange ? formatValue(valueRange.max) : ''}</span>
						<span>{valueRange ? formatValue((valueRange.min + valueRange.max) / 2) : ''}</span>
						<span>{valueRange ? formatValue(valueRange.min) : ''}</span>
					</div>
					<Tooltip.Provider>
						<Tooltip.Root open={hoverCell !== null}>
							<Tooltip.Trigger>
								{#snippet child({ props })}
									<svg
										{...props}
										viewBox="0 0 {CHART_WIDTH} {CHART_HEIGHT}"
										preserveAspectRatio="none"
										class="h-[140px] w-full cursor-pointer"
										role="img"
										aria-label="{selectedKey} distribution over time - click a column to filter logs to that time range"
										onpointermove={handlePointerMove}
										onpointerleave={() => (hoverCell = null)}
										onclick={handleClick}
									>
										{#each [PEAK_Y, (PEAK_Y + BASELINE_Y) / 2, BASELINE_Y] as gridY (gridY)}
											<line
												x1="0"
												y1={gridY}
												x2={CHART_WIDTH}
												y2={gridY}
												class="text-border"
												stroke="currentColor"
												stroke-width="1"
												vector-effect="non-scaling-stroke"
											/>
										{/each}

										{#each grid as column, timeIndex (timeIndex)}
											{#each column as count, valueIndex (valueIndex)}
												{@const color = densityColor(count, maxCellCount)}
												{#if color}
													{@const rect = cellRect(timeIndex, valueIndex)}
													<rect
														x={rect.x}
														y={rect.y}
														width={rect.width}
														height={rect.height}
														fill={color}
														fill-opacity={hoverCell?.timeIndex === timeIndex ? 1 : 0.85}
													/>
												{/if}
											{/each}
										{/each}
									</svg>
								{/snippet}
							</Tooltip.Trigger>
							{#if hoverCell !== null}
								<Tooltip.Content>
									{bucketTimeLabel(hoverCell.timeIndex)} · {selectedKey} {bucketValueLabel(hoverCell.valueIndex)} ·
									{grid[hoverCell.timeIndex]?.[hoverCell.valueIndex] ?? 0} events · click to filter
								</Tooltip.Content>
							{/if}
						</Tooltip.Root>
					</Tooltip.Provider>

					<div></div>
					<div class="text-muted-foreground mt-1 flex justify-between text-[10px]">
						<span>{rangeFrom ? formatAxisTime(rangeFrom) : ''}</span>
						<span>{rangeTo ? formatAxisTime(rangeTo) : ''}</span>
					</div>
				</div>
			{/if}
		</Accordion.Content>
	</Accordion.Item>
</Accordion.Root>
