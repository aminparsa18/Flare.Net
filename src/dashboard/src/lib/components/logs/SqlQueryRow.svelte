<script lang="ts">
	// Seq-style SQL query bar: `select count(*)|* from stream [where ...] [group by
	// time(1h)[, service|level]]`, parsed/translated server-side (see Flare.Api's
	// Query/LogQl/LogQlParser.cs) - never raw-passed to ClickHouse. Own Accordion
	// section, same pattern VolumeChart/ValueDistributionChart already use, but
	// collapsed by default (ValueDistributionChart's own reasoning: a power-user
	// extra, shouldn't claim vertical space above the log table until opened once)
	// and explicit Run only - no reactive $effect re-running it on every filter/keystroke
	// change, matching Seq's own SQL bar and avoiding a query per keystroke.
	import { tick } from 'svelte';
	import { browser } from '$app/environment';
	import { runLogQlQuery, type LogAggregateBucket, type LogQlQueryResponse } from '$lib/api';
	import { resolveTimeRange } from '$lib/logs/time-range';
	import { logsExplorerContext } from '$lib/logs/context';
	import { tokenizeLogQl, TOKEN_COLOR_VARS } from '$lib/logs/sql-highlight';
	import { getLogQlSuggestions, type LogQlSuggestion, type LogQlSuggestionResult } from '$lib/logs/sql-autocomplete';
	import * as Accordion from '$lib/components/ui/accordion';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Button } from '$lib/components/ui/button';
	import PlayIcon from '@lucide/svelte/icons/play';
	import XIcon from '@lucide/svelte/icons/x';

	const explorer = logsExplorerContext.get();

	const LIVE_TRAILING_WINDOW_MS = 60 * 60 * 1000; // matches VolumeChart's own live-mode window - see its remarks

	const ITEM = 'sql-query';
	const COLLAPSE_STORAGE_KEY = 'flare.logs.sqlQueryCollapsed';
	const QUERY_STORAGE_KEY = 'flare.logs.sqlQueryText';

	function loadStoredValue(): string {
		if (!browser) return ''; // collapsed by default - see this file's own header remarks
		try {
			return localStorage.getItem(COLLAPSE_STORAGE_KEY) === 'false' ? ITEM : '';
		} catch {
			return ''; // storage disabled (e.g. private browsing) - fall back to collapsed
		}
	}

	function loadStoredQuery(): string {
		if (!browser) return '';
		try {
			return localStorage.getItem(QUERY_STORAGE_KEY) ?? '';
		} catch {
			return '';
		}
	}

	let accordionValue = $state(loadStoredValue());
	let queryText = $state(loadStoredQuery());
	let running = $state(false);
	let error = $state<string | null>(null);
	let result = $state<LogQlQueryResponse | null>(null);

	// Syntax highlighting: a `<pre>` of colored spans rendered directly behind the
	// textarea, which itself goes transparent-text-on-transparent-background so only its
	// caret/selection show - the standard "highlighted textarea" overlay technique (same
	// one react-simple-code-editor and friends use), picked over pulling in a full code-
	// editor dependency since this is one input box, not a whole editing surface, and
	// this project has no highlighter dependency installed anywhere else either (see
	// CodeBlock.svelte's own "no highlighter dependency" remarks).
	const highlightTokens = $derived(tokenizeLogQl(queryText));
	let highlightEl: HTMLPreElement | null = $state(null);

	// Keeps the (invisible-text) textarea and the highlight layer scrolling together -
	// only reachable in practice if the textarea's field-sizing-content growth (see its
	// own min-h-24 below) somehow can't keep up and it scrolls internally instead, but
	// cheap enough to wire up unconditionally rather than assume that never happens.
	function syncHighlightScroll(e: Event) {
		const textarea = e.currentTarget as HTMLTextAreaElement;
		if (!highlightEl) return;
		highlightEl.scrollTop = textarea.scrollTop;
		highlightEl.scrollLeft = textarea.scrollLeft;
	}

	// Autocomplete: so a user who doesn't already know the grammar can discover what's
	// available (keywords/columns/functions) as they type - see sql-autocomplete.ts for
	// the actual context walk. Recomputed imperatively (not a $derived off queryText)
	// since it also depends on caret position, a plain DOM property with no reactive
	// signal of its own - every place the caret can move (typing, click, arrow keys)
	// calls updateSuggestions() directly instead.
	let textareaEl: HTMLTextAreaElement | null = $state(null);
	let suggestionResult = $state<LogQlSuggestionResult | null>(null);
	let activeSuggestionIndex = $state(0);

	// Caret-position mirror: an invisible clone of the textarea's text (same font/
	// padding/wrap - see its own markup below) up to the caret, ending in a zero-width
	// marker span. Measuring that span's viewport position (not the textarea/wrapper's
	// own box) is what lets the dropdown open right under the line being typed rather
	// than under the whole (now multi-line) box - the same "hidden mirror element"
	// technique textarea-caret-position libraries use, just enough of it hand-rolled
	// here for one marker instead of a full x/y-per-character API.
	let caretMirrorEl: HTMLPreElement | null = $state(null);
	let caretMarkerEl: HTMLSpanElement | null = $state(null);
	let caretBeforeText = $state('');
	let dropdownPosition = $state({ top: 0, left: 0 });

	function updateSuggestions() {
		if (!textareaEl) {
			suggestionResult = null;
			return;
		}
		const cursorPos = textareaEl.selectionStart ?? queryText.length;
		caretBeforeText = queryText.slice(0, cursorPos);
		const next = getLogQlSuggestions(queryText, cursorPos);
		suggestionResult = next.suggestions.length > 0 ? next : null;
		activeSuggestionIndex = 0;
	}

	// Recomputes the dropdown's fixed (viewport-relative, not wrapper-relative) position
	// whenever it opens or the caret moves - `position: fixed` from the marker's own
	// getBoundingClientRect (not the wrapper's), because Accordion.Content's own
	// overflow-hidden (needed for its collapse/expand animation - see accordion-
	// content.svelte) would otherwise clip an absolutely-positioned dropdown the instant
	// it grows past the accordion's own rendered height, same as it was doing before
	// this fix (the dropdown was rendering, just clipped under whatever came next).
	$effect(() => {
		if (!suggestionResult || !caretMarkerEl) return;
		void caretBeforeText; // establishes the dependency - the marker's position only reflects this after it re-renders
		const rect = caretMarkerEl.getBoundingClientRect();
		const lineHeight = textareaEl ? parseFloat(getComputedStyle(textareaEl).lineHeight) || 20 : 20;
		dropdownPosition = { top: rect.top + lineHeight, left: rect.left };
	});

	/** Replaces the in-progress word (suggestionResult.wordStart..caret) with the accepted suggestion's text. */
	async function acceptSuggestion(suggestion: LogQlSuggestion) {
		if (!suggestionResult || !textareaEl) return;
		const cursorPos = textareaEl.selectionStart ?? queryText.length;
		const before = queryText.slice(0, suggestionResult.wordStart);
		const after = queryText.slice(cursorPos);
		queryText = before + suggestion.insertText + after;
		const newCaretPos = before.length + suggestion.insertText.length;
		suggestionResult = null;
		// bind:value only reflects the new text into the DOM after this tick - the caret
		// position has to be set after that, or it's clamped against the still-stale value.
		await tick();
		textareaEl.focus();
		textareaEl.setSelectionRange(newCaretPos, newCaretPos);
		updateSuggestions();
	}

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

	// Literal copy of VolumeChart.svelte's own currentRange() (live -> trailing window,
	// else resolveTimeRange) - same duplication precedent ValueDistributionChart.svelte
	// already follows rather than extracting a shared util. Deliberately not
	// explorer.currentRange() (LogsExplorerState's own method): that one also respects a
	// VolumeChart bar-click selection and doesn't special-case live mode, neither of
	// which apply to this row's own independent time bound.
	function currentRange(): { from: string; to: string } {
		if (explorer.live) {
			const to = new Date();
			return { from: new Date(to.getTime() - LIVE_TRAILING_WINDOW_MS).toISOString(), to: to.toISOString() };
		}
		const range = resolveTimeRange(explorer.filter.timeRangePreset, explorer.filter.customRange ?? undefined);
		if (range) return range;
		const to = new Date();
		return { from: new Date(to.getTime() - LIVE_TRAILING_WINDOW_MS).toISOString(), to: to.toISOString() };
	}

	async function run() {
		const query = queryText.trim();
		if (!query || running) return;
		running = true;
		error = null;
		suggestionResult = null;
		if (browser) {
			try {
				localStorage.setItem(QUERY_STORAGE_KEY, queryText);
			} catch {
				// Non-critical - just isn't remembered for the next reload.
			}
		}
		try {
			const range = currentRange();
			result = await runLogQlQuery({ query, from: range.from, to: range.to });
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
			result = null;
		} finally {
			running = false;
		}
	}

	// When the suggestion dropdown is open, arrow keys move the highlighted row and
	// Enter/Tab accept it instead of their usual meaning (run / leave the field) -
	// standard combobox keyboard convention. Otherwise: Enter runs the query (same as
	// clicking Run); Shift+Enter falls through to the textarea's own default behavior and
	// inserts a newline instead (Slack/ChatGPT's submit-vs-newline convention).
	function handleKeydown(e: KeyboardEvent) {
		if (suggestionResult) {
			const { suggestions } = suggestionResult;
			if (e.key === 'ArrowDown') {
				e.preventDefault();
				activeSuggestionIndex = (activeSuggestionIndex + 1) % suggestions.length;
				return;
			}
			if (e.key === 'ArrowUp') {
				e.preventDefault();
				activeSuggestionIndex = (activeSuggestionIndex - 1 + suggestions.length) % suggestions.length;
				return;
			}
			if (e.key === 'Enter' || e.key === 'Tab') {
				e.preventDefault();
				void acceptSuggestion(suggestions[activeSuggestionIndex]);
				return;
			}
			if (e.key === 'Escape') {
				suggestionResult = null;
				return;
			}
		}

		if (e.key === 'Enter' && !e.shiftKey) {
			e.preventDefault();
			run();
		}
	}

	function clearQuery() {
		queryText = '';
		result = null;
		error = null;
		suggestionResult = null;
	}

	// ---- Series (grouped-by-time count) rendering -----------------------------

	const seriesBuckets = $derived<LogAggregateBucket[]>(result?.kind === 'Series' ? (result.buckets ?? []) : []);
	const hasGroupKey = $derived(seriesBuckets.some((b) => b.groupKey != null));
	const totalSeriesCount = $derived(seriesBuckets.reduce((sum, b) => sum + b.count, 0));

	const bucketStarts = $derived.by(() => {
		const set = new Set(seriesBuckets.map((b) => b.bucketStart));
		return [...set].sort();
	});

	const bucketTotals = $derived.by(() => {
		const totals = new Map<string, number>();
		for (const b of seriesBuckets) {
			totals.set(b.bucketStart, (totals.get(b.bucketStart) ?? 0) + b.count);
		}
		return totals;
	});

	const CATEGORY_COLORS = ['#3987e5', '#e8a33d', '#4caf7d', '#c264d8', '#e0555f', '#59b8c9', '#a3a3a3', '#8a6fd1'];
	const MAX_SERIES = 7; // + a capped "Other" bucket for anything past this - keeps the legend/chart legible for a high-cardinality group-by column

	// Only meaningful when hasGroupKey - the distinct group keys to render as separate
	// stacked segments, ranked by total count and capped (rest folded into "Other").
	const seriesKeys = $derived.by(() => {
		if (!hasGroupKey) return [];
		const totals = new Map<string, number>();
		for (const b of seriesBuckets) {
			const key = b.groupKey ?? '';
			totals.set(key, (totals.get(key) ?? 0) + b.count);
		}
		const ranked = [...totals.entries()].sort((a, b) => b[1] - a[1]).map(([k]) => k);
		return ranked.length > MAX_SERIES ? [...ranked.slice(0, MAX_SERIES), 'Other'] : ranked;
	});

	function keyFor(groupKey: string | null): string {
		const key = groupKey ?? '';
		return hasGroupKey && !seriesKeys.includes(key) ? 'Other' : key;
	}

	function colorFor(key: string): string {
		if (!hasGroupKey) return 'var(--primary)';
		const index = seriesKeys.indexOf(key);
		return CATEGORY_COLORS[index % CATEGORY_COLORS.length] ?? CATEGORY_COLORS[CATEGORY_COLORS.length - 1];
	}

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 100;
	const BASELINE_Y = CHART_HEIGHT - 2;
	const PEAK_Y = 3;

	const peakBucketCount = $derived(Math.max(0, ...[...bucketTotals.values()]));
	const maxBucketCount = $derived(Math.max(1, peakBucketCount));
	const barWidth = $derived(CHART_WIDTH / Math.max(1, bucketStarts.length));

	/** Stacked segments (bottom-up) for one bucket's bar, each already scaled to chart-space height. */
	function barSegments(bucketStart: string): { height: number; color: string }[] {
		const total = bucketTotals.get(bucketStart) ?? 0;
		if (total === 0) return [];
		const totalHeight = (total / maxBucketCount) * (BASELINE_Y - PEAK_Y);

		const segTotals = new Map<string, number>();
		for (const b of seriesBuckets) {
			if (b.bucketStart !== bucketStart) continue;
			const key = keyFor(b.groupKey);
			segTotals.set(key, (segTotals.get(key) ?? 0) + b.count);
		}

		const orderedKeys = hasGroupKey ? seriesKeys : [''];
		return orderedKeys
			.map((key) => ({ height: ((segTotals.get(key) ?? 0) / total) * totalHeight, color: colorFor(key) }))
			.filter((seg) => seg.height > 0);
	}

	function formatBucketTime(iso: string): string {
		return new Date(iso).toLocaleString(undefined, {
			hour12: false,
			month: 'short',
			day: 'numeric',
			hour: '2-digit',
			minute: '2-digit'
		});
	}

	function formatRowTime(iso: string): string {
		return new Date(iso).toLocaleString(undefined, {
			hour12: false,
			month: 'short',
			day: 'numeric',
			hour: '2-digit',
			minute: '2-digit',
			second: '2-digit'
		});
	}

	// maximumFractionDigits caps avg()'s fractional results (e.g. avg(SeverityNumber) ->
	// 12.399999999999998) at 2 decimals - a no-op for plain counts, which are always
	// whole (Intl doesn't pad trailing zeros without a matching minimumFractionDigits).
	const compactCount = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 2 });
	function formatCount(n: number): string {
		return compactCount.format(n);
	}
</script>

<Accordion.Root type="single" bind:value={accordionValue} class="w-full flex-col rounded-none border-0 border-b">
	<Accordion.Item value={ITEM} class="border-0 data-open:bg-transparent">
		<div class="flex items-center justify-between gap-2 px-4 py-3 text-xs">
			<Accordion.Trigger
				class="text-muted-foreground hover:text-foreground group/accordion-trigger relative flex w-auto flex-none items-center justify-start gap-1 border-none p-0 text-left text-xs font-normal hover:no-underline **:data-[slot=accordion-trigger-icon]:ml-0 **:data-[slot=accordion-trigger-icon]:size-3.5"
			>
				SQL query
			</Accordion.Trigger>
			{#if !collapsed && result}
				<span class="text-muted-foreground tabular-nums">
					{#if result.kind === 'Count'}
						{formatCount(result.count ?? 0)} events
					{:else if result.kind === 'Series'}
						{formatCount(totalSeriesCount)} events
					{:else if result.kind === 'Rows'}
						{formatCount(result.events?.length ?? 0)}{result.hasMoreRows ? '+' : ''} events
					{:else}
						{formatCount(result.rows?.length ?? 0)}{result.hasMoreRows ? '+' : ''} rows
					{/if}
				</span>
			{/if}
		</div>
		<Accordion.Content class="px-4 pb-3">
			<div class="flex items-start gap-2">
				<div class="relative flex-1">
					<!-- Highlight layer - purely decorative (aria-hidden, no pointer events); the
					     real textarea beneath owns the actual value/selection/a11y. Same padding/
					     border/font/wrap classes as the Textarea below so the two stay pixel-aligned. -->
					<pre
						bind:this={highlightEl}
						aria-hidden="true"
						class="pointer-events-none absolute inset-0 m-0 overflow-hidden rounded-md border border-transparent px-2 py-2 font-mono text-sm break-words whitespace-pre-wrap md:text-xs/relaxed"
					>{#each highlightTokens as token, i (i)}<span style={TOKEN_COLOR_VARS[token.type] ? `color: ${TOKEN_COLOR_VARS[token.type]}` : undefined}>{token.text}</span>{/each}</pre>
					<!-- Invisible caret-position mirror - see dropdownPosition's own remarks. Not
					     the same element as the highlight layer above (that one's spans are keyed
					     by token, not by caret offset, so splicing a marker into it isn't simple). -->
					<pre
						bind:this={caretMirrorEl}
						aria-hidden="true"
						class="invisible pointer-events-none absolute inset-0 m-0 overflow-hidden rounded-md border border-transparent px-2 py-2 font-mono text-sm break-words whitespace-pre-wrap md:text-xs/relaxed"
					>{caretBeforeText}<span bind:this={caretMarkerEl}></span></pre>
					<Textarea
						bind:ref={textareaEl}
						class="min-h-24 relative bg-transparent pr-7 font-mono text-transparent"
						style="caret-color: var(--foreground);"
						placeholder="select count(*) from stream group by time(1h)"
						bind:value={queryText}
						onkeydown={handleKeydown}
						onscroll={syncHighlightScroll}
						oninput={updateSuggestions}
						onclick={updateSuggestions}
						onkeyup={updateSuggestions}
						onblur={() => (suggestionResult = null)}
						role="combobox"
						aria-autocomplete="list"
						aria-expanded={suggestionResult !== null}
						aria-controls="sql-query-suggestions"
					/>
					{#if queryText}
						<button
							type="button"
							class="text-muted-foreground hover:text-foreground absolute top-2 right-2"
							onclick={clearQuery}
							aria-label="Clear query"
						>
							<XIcon class="size-3.5" />
						</button>
					{/if}
					{#if suggestionResult}
						<!-- mousedown (not click), with preventDefault, so picking a suggestion never
						     blurs the textarea first - that would fire the onblur close handler above
						     before the click itself is ever handled.

						     position: fixed (viewport coordinates from dropdownPosition, not a
						     wrapper-relative absolute) so this escapes Accordion.Content's own
						     overflow-hidden (its collapse/expand animation - see accordion-
						     content.svelte) instead of being clipped under whatever renders next
						     (the log table) the moment it grows past the accordion's own height.
						     z-50 matches this app's own Popover/Select content (see their z-50). -->
						<ul
							id="sql-query-suggestions"
							role="listbox"
							style="top: {dropdownPosition.top}px; left: {dropdownPosition.left}px;"
							class="bg-popover text-popover-foreground fixed z-50 max-h-48 w-64 overflow-auto rounded-md border py-1 shadow-md"
						>
							{#each suggestionResult.suggestions as suggestion, i (suggestion.label)}
								<li role="presentation">
									<button
										type="button"
										role="option"
										aria-selected={i === activeSuggestionIndex}
										class={[
											'flex w-full items-baseline justify-between gap-3 px-2 py-1 text-left font-mono text-xs',
											i === activeSuggestionIndex ? 'bg-accent text-accent-foreground' : 'hover:bg-accent/50'
										]}
										onmousedown={(e) => {
											e.preventDefault();
											void acceptSuggestion(suggestion);
										}}
										onmouseenter={() => (activeSuggestionIndex = i)}
									>
										<span>{suggestion.label}</span>
										{#if suggestion.detail}
											<span class="text-muted-foreground shrink-0 font-sans text-[10px]">{suggestion.detail}</span>
										{/if}
									</button>
								</li>
							{/each}
						</ul>
					{/if}
				</div>
				<Button
					variant="outline"
					size="icon-sm"
					disabled={!queryText.trim() || running}
					onclick={run}
					title="Run query (Enter - use Shift+Enter for a new line)"
					aria-label="Run query"
				>
					<PlayIcon class="text-primary" />
				</Button>
			</div>

			{#if error}
				<p class="text-destructive mt-2 text-xs">{error}</p>
			{:else if result?.kind === 'Count'}
				<div class="mt-2 flex h-16 items-baseline">
					<span class="text-2xl font-semibold tabular-nums">{formatCount(result.count ?? 0)}</span>
					<span class="text-muted-foreground ml-2 text-xs">events</span>
				</div>
			{:else if result?.kind === 'Series'}
				{#if bucketStarts.length === 0}
					<div class="text-muted-foreground mt-2 flex h-[100px] items-center justify-center text-xs">No data</div>
				{:else}
					<div class="mt-2 grid grid-cols-[2.5rem_1fr] gap-x-2">
						<div class="text-muted-foreground flex h-[100px] flex-col justify-between py-0.5 text-right text-[10px] tabular-nums">
							<span>{formatCount(peakBucketCount)}</span>
							<span>{formatCount(Math.round(peakBucketCount / 2))}</span>
							<span>0</span>
						</div>
						<svg viewBox="0 0 {CHART_WIDTH} {CHART_HEIGHT}" preserveAspectRatio="none" class="h-[100px] w-full" role="img" aria-label="Query result over time">
							{#each [PEAK_Y, (PEAK_Y + BASELINE_Y) / 2, BASELINE_Y] as gridY (gridY)}
								<line x1="0" y1={gridY} x2={CHART_WIDTH} y2={gridY} class="text-border" stroke="currentColor" stroke-width="1" vector-effect="non-scaling-stroke" />
							{/each}
							{#each bucketStarts as bucketStart, i (bucketStart)}
								{@const x = i * barWidth + 1}
								{@const width = Math.max(1, barWidth - 2)}
								{@const total = bucketTotals.get(bucketStart) ?? 0}
								{@const segments = barSegments(bucketStart)}
								<g>
									<title>{formatBucketTime(bucketStart)} · {formatCount(total)} events</title>
									{#each segments as segment, si (si)}
										{@const priorHeight = segments.slice(0, si).reduce((sum, s) => sum + s.height, 0)}
										<rect {x} y={BASELINE_Y - priorHeight - segment.height} {width} height={Math.max(0.5, segment.height)} fill={segment.color} fill-opacity="0.85" />
									{/each}
								</g>
							{/each}
						</svg>
						<div></div>
						<div class="text-muted-foreground mt-1 flex justify-between text-[10px]">
							<span>{bucketStarts[0] ? formatBucketTime(bucketStarts[0]) : ''}</span>
							<span>{bucketStarts.at(-1) ? formatBucketTime(bucketStarts.at(-1)!) : ''}</span>
						</div>
					</div>
					{#if hasGroupKey}
						<div class="mt-2 flex flex-wrap gap-x-3 gap-y-1">
							{#each seriesKeys as key (key)}
								<span class="text-muted-foreground flex items-center gap-1 text-[10px]">
									<span class="size-2 rounded-sm" style="background: {colorFor(key)};"></span>
									{key || '(none)'}
								</span>
							{/each}
						</div>
					{/if}
				{/if}
			{:else if result?.kind === 'Rows'}
				{#if (result.events?.length ?? 0) === 0}
					<div class="text-muted-foreground mt-2 flex h-16 items-center justify-center text-xs">No matching events</div>
				{:else}
					<div class="mt-2 max-h-64 overflow-auto rounded border">
						<table class="w-full text-left text-xs">
							<thead class="bg-muted/50 sticky top-0">
								<tr>
									<th class="px-2 py-1 font-medium">Time</th>
									<th class="px-2 py-1 font-medium">Service</th>
									<th class="px-2 py-1 font-medium">Level</th>
									<th class="px-2 py-1 font-medium">Body</th>
								</tr>
							</thead>
							<tbody>
								{#each result.events ?? [] as event (event.eventId)}
									<tr class="border-t">
										<td class="text-muted-foreground px-2 py-1 whitespace-nowrap tabular-nums">{formatRowTime(event.timestamp)}</td>
										<td class="px-2 py-1 whitespace-nowrap">{event.serviceName}</td>
										<td class="px-2 py-1 whitespace-nowrap">{event.severityText}</td>
										<td class="max-w-0 truncate px-2 py-1" title={event.body}>{event.body}</td>
									</tr>
								{/each}
							</tbody>
						</table>
					</div>
					{#if result.hasMoreRows}
						<p class="text-muted-foreground mt-1 text-[10px]">
							Showing first {result.events?.length ?? 0} — narrow your query or time range for more.
						</p>
					{/if}
				{/if}
			{:else if result?.kind === 'Table'}
				{#if (result.rows?.length ?? 0) === 0}
					<div class="text-muted-foreground mt-2 flex h-16 items-center justify-center text-xs">No matching events</div>
				{:else}
					<div class="mt-2 max-h-64 overflow-auto rounded border">
						<table class="w-full text-left text-xs">
							<thead class="bg-muted/50 sticky top-0">
								<tr>
									{#each result.columns ?? [] as column (column)}
										<th class="px-2 py-1 font-medium whitespace-nowrap">{column}</th>
									{/each}
								</tr>
							</thead>
							<tbody>
								{#each result.rows ?? [] as row, ri (ri)}
									<tr class="border-t">
										{#each row as cell, ci (ci)}
											<td class="max-w-64 truncate px-2 py-1" title={cell}>{cell}</td>
										{/each}
									</tr>
								{/each}
							</tbody>
						</table>
					</div>
					{#if result.hasMoreRows}
						<p class="text-muted-foreground mt-1 text-[10px]">
							Showing first {result.rows?.length ?? 0} — narrow your query or time range for more.
						</p>
					{/if}
				{/if}
			{/if}
		</Accordion.Content>
	</Accordion.Item>
</Accordion.Root>
