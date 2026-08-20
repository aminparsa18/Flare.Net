<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { browser } from '$app/environment';
	import { MetricsExplorerState } from '$lib/metrics/state.svelte';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import { resolveRequestedSavedView } from '$lib/saved-views/hydrate';
	import MetricsToolbar from '$lib/components/metrics/MetricsToolbar.svelte';
	import MetricPicker from '$lib/components/metrics/MetricPicker.svelte';
	import MetricChart from '$lib/components/metrics/MetricChart.svelte';

	const explorer = metricsExplorerContext.set(new MetricsExplorerState());

	onDestroy(() => {
		explorer.dispose();
	});

	// Drag-resizable divider between the Metric Picker (left) and the chart (right) - a
	// plain pointer-capture drag rather than a resizable-panes library: this is the only
	// split pane in the app so far, and the drag itself is short enough that pulling in a
	// dependency (e.g. paneforge) for it isn't worth it yet - same "hand-roll it, no
	// library" precedent VolumeChart/MetricChart/TraceWaterfall already set for charts.
	// Persisted to localStorage (not the explorer's own filter state, which is about
	// what's queried, not how it's laid out) so a chosen width survives reloads, same
	// convention $lib/logs/recent-searches.ts already uses for a lightweight UI
	// preference.
	const STORAGE_KEY = 'flare.metrics.pickerWidth';
	const MIN_WIDTH = 200;
	const MAX_WIDTH = 560;
	const DEFAULT_WIDTH = 288; // the fixed `w-72` MetricPicker used before this existed

	function clampWidth(px: number): number {
		return Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, px));
	}

	function loadStoredWidth(): number {
		if (!browser) return DEFAULT_WIDTH;
		try {
			const raw = localStorage.getItem(STORAGE_KEY);
			const parsed = raw ? Number(raw) : NaN;
			return Number.isFinite(parsed) ? clampWidth(parsed) : DEFAULT_WIDTH;
		} catch {
			return DEFAULT_WIDTH; // storage disabled (e.g. private browsing) - fall back silently
		}
	}

	function persistWidth(px: number): void {
		try {
			localStorage.setItem(STORAGE_KEY, String(px));
		} catch {
			// Non-critical - the next reload just falls back to DEFAULT_WIDTH instead.
		}
	}

	let pickerWidth = $state(DEFAULT_WIDTH);
	let panelsEl: HTMLDivElement | null = null;
	let dragging = $state(false);

	onMount(() => {
		pickerWidth = loadStoredWidth();

		void (async () => {
			// ?view=<id> (a saved view's shareable link) takes priority - applySavedViewState
			// already loads names + re-selects the saved metric itself, so loadNames() below
			// is only reached with no (or an invalid) view id.
			const view = await resolveRequestedSavedView(page.url, 'Metrics');
			if (view) {
				await explorer.applySavedViewState(view.state);
			} else {
				void explorer.loadNames();
			}
			void explorer.loadKnownServices();
		})();
	});

	function startDrag(e: PointerEvent): void {
		dragging = true;
		document.body.style.cursor = 'col-resize';
		document.body.style.userSelect = 'none';
		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
	}

	function onDrag(e: PointerEvent): void {
		if (!dragging || !panelsEl) return;
		pickerWidth = clampWidth(e.clientX - panelsEl.getBoundingClientRect().left);
	}

	function endDrag(): void {
		if (!dragging) return;
		dragging = false;
		document.body.style.cursor = '';
		document.body.style.userSelect = '';
		persistWidth(pickerWidth);
	}

	// Keyboard equivalent of the drag - a `role="separator"` divider is only actually
	// operable, not just labeled, once it responds to the arrow keys the ARIA pattern
	// promises. Shift widens the step, same "hold for a bigger jump" convention most
	// resize UIs use.
	function handleKeydown(e: KeyboardEvent): void {
		const step = e.shiftKey ? 32 : 8;
		if (e.key === 'ArrowLeft') {
			e.preventDefault();
			pickerWidth = clampWidth(pickerWidth - step);
			persistWidth(pickerWidth);
		} else if (e.key === 'ArrowRight') {
			e.preventDefault();
			pickerWidth = clampWidth(pickerWidth + step);
			persistWidth(pickerWidth);
		}
	}
</script>

<svelte:head>
	<title>Flare — Metrics</title>
</svelte:head>

<div class="flex h-full flex-col">
	<MetricsToolbar />
	<div class="flex min-h-0 flex-1" bind:this={panelsEl}>
		<MetricPicker width={pickerWidth} />
		<!-- svelte-ignore a11y_no_noninteractive_tabindex -->
		<!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
		<!-- Svelte's linter treats role="separator" as non-interactive by default, but the
		     ARIA APG's "Separator (Focusable)" pattern (https://www.w3.org/WAI/ARIA/apg/patterns/windowsplitter/)
		     calls for exactly this on a window-splitter/resize-handle: tabindex + arrow-key
		     handling on the separator itself, plus aria-valuenow/min/max so it reads as a
		     real control, not decoration - same justified suppression VirtualList.svelte
		     already uses for its own ARIA APG pattern (a scrollable region). -->
		<div
			role="separator"
			aria-orientation="vertical"
			aria-label="Resize metric list"
			aria-valuenow={pickerWidth}
			aria-valuemin={MIN_WIDTH}
			aria-valuemax={MAX_WIDTH}
			tabindex="0"
			class="group relative w-1.5 shrink-0 cursor-col-resize touch-none self-stretch outline-none select-none"
			onpointerdown={startDrag}
			onpointermove={onDrag}
			onpointerup={endDrag}
			onkeydown={handleKeydown}
		>
			<div
				class="bg-border group-hover:bg-primary/50 absolute inset-y-0 left-1/2 w-px -translate-x-1/2 transition-colors {dragging
					? 'bg-primary'
					: ''}"
			></div>
		</div>
		<MetricChart />
	</div>
</div>
