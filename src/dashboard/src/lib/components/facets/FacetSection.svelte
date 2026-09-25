<script lang="ts">
	// One collapsible facet list in FacetSidebar - fetches its own options (so a slow
	// high-cardinality attribute doesn't hold up Service/Severity) and re-fetches whenever
	// the sidebar's `reloadKey` changes. Collapsed sections don't fetch at all.
	import { untrack } from 'svelte';
	import ChevronRightIcon from '@lucide/svelte/icons/chevron-right';
	import XIcon from '@lucide/svelte/icons/x';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import { Spinner } from '$lib/components/ui/spinner';
	import { formatCount } from '$lib/ingestion/format';
	import type { FacetDefinition, FacetOption } from '$lib/facets/types';
	import * as m from '$lib/paraglide/messages';

	let { facet, reloadKey }: { facet: FacetDefinition; reloadKey: string } = $props();

	/** Rows shown before "Show more" - the fetch itself returns up to the facet's own limit. */
	const COLLAPSED_ROWS = 8;

	let expanded = $state(true);
	let showAll = $state(false);
	let options = $state.raw<FacetOption[]>([]);
	let loading = $state(false);
	let error = $state<string | null>(null);

	$effect(() => {
		void reloadKey;
		if (!expanded) return;
		const abort = new AbortController();
		loading = true;
		error = null;
		// Untracked: `load` reads the explorer's filter state to build its request, and
		// those reads must not become dependencies of this effect - `reloadKey` is the one
		// deliberate trigger (a new `facet` object arrives on every filter change too).
		untrack(() => facet.load(abort.signal))
			.then((result) => {
				if (abort.signal.aborted) return;
				options = result;
				loading = false;
			})
			.catch((err: unknown) => {
				if (abort.signal.aborted) return;
				error = err instanceof Error ? err.message : String(err);
				loading = false;
			});
		return () => abort.abort();
	});

	function labelFor(value: string): string {
		return facet.label?.(value) ?? value;
	}

	// Selected values the latest fetch didn't return (zero matches under the other
	// facets' filters, or past the fetch limit) still get a row, so they can be unticked.
	const rows = $derived.by(() => {
		const returned = new Set(options.map((o) => o.value));
		const missing = facet.selected.filter((v) => !returned.has(v)).map((value) => ({ value, count: 0 }));
		return [...options, ...missing];
	});
	const visibleRows = $derived(showAll ? rows : rows.slice(0, COLLAPSED_ROWS));
	const maxCount = $derived(Math.max(1, ...rows.map((r) => r.count)));

	function toggle(value: string): void {
		const isSelected = facet.selected.includes(value);
		if (facet.single) {
			facet.onChange(isSelected ? [] : [value]);
		} else {
			facet.onChange(isSelected ? facet.selected.filter((v) => v !== value) : [...facet.selected, value]);
		}
	}
</script>

<section class="border-b">
	<div class="flex items-center gap-1 px-3 py-2">
		<button
			type="button"
			class="text-muted-foreground hover:text-foreground flex min-w-0 flex-1 items-center gap-1 text-left text-xs font-medium"
			aria-expanded={expanded}
			onclick={() => (expanded = !expanded)}
		>
			<ChevronRightIcon class="size-3.5 shrink-0 transition-transform {expanded ? 'rotate-90' : ''}" />
			<span class="truncate" title={facet.title}>{facet.title}</span>
			{#if facet.selected.length > 0}
				<span class="bg-primary text-primary-foreground rounded px-1 text-[10px] tabular-nums">{facet.selected.length}</span>
			{/if}
		</button>
		{#if loading}
			<Spinner class="text-muted-foreground size-3" />
		{/if}
		{#if facet.selected.length > 0}
			<button type="button" class="text-muted-foreground hover:text-foreground text-[11px]" onclick={() => facet.onChange([])}>
				{m.facets_clear()}
			</button>
		{/if}
		{#if facet.onRemove}
			<button
				type="button"
				class="text-muted-foreground hover:text-foreground"
				aria-label={m.facets_removeFacet({ facet: facet.title })}
				title={m.facets_removeFacet({ facet: facet.title })}
				onclick={facet.onRemove}
			>
				<XIcon class="size-3.5" />
			</button>
		{/if}
	</div>

	{#if expanded}
		<div class="pb-2">
			{#if error}
				<p class="text-destructive px-3 text-xs">{error}</p>
			{:else if rows.length === 0 && !loading}
				<p class="text-muted-foreground px-3 text-xs">{m.facets_noValues()}</p>
			{/if}
			{#each visibleRows as row (row.value)}
				{@const checked = facet.selected.includes(row.value)}
				{@const label = labelFor(row.value)}
				<div class="group/facet hover:bg-muted/60 relative flex items-center gap-2 px-3 py-1 text-xs">
					<!-- Relative-count bar behind the row, same "share of the max" idea as a histogram. -->
					<div class="bg-primary/10 pointer-events-none absolute inset-y-0.5 left-0 rounded-r" style="width: {(row.count / maxCount) * 100}%"></div>
					<Checkbox checked={checked} onCheckedChange={() => toggle(row.value)} aria-label={label} class="relative" />
					<button
						type="button"
						tabindex="-1"
						class="relative min-w-0 flex-1 truncate text-left {row.value === '' ? 'text-muted-foreground italic' : ''}"
						title={label}
						onclick={() => toggle(row.value)}
					>
						{row.value === '' ? m.facets_emptyValue() : label}
					</button>
					{#if !facet.single}
						<button
							type="button"
							class="text-muted-foreground hover:text-foreground relative hidden text-[11px] group-hover/facet:inline focus-visible:inline"
							onclick={() => facet.onChange([row.value])}
						>
							{m.facets_only()}
						</button>
					{/if}
					<span class="text-muted-foreground relative tabular-nums">{formatCount(row.count)}</span>
				</div>
			{/each}
			{#if rows.length > COLLAPSED_ROWS}
				<button type="button" class="text-muted-foreground hover:text-foreground px-3 pt-1 text-[11px]" onclick={() => (showAll = !showAll)}>
					{showAll ? m.facets_showLess() : m.facets_showMore({ count: rows.length - COLLAPSED_ROWS })}
				</button>
			{/if}
		</div>
	{/if}
</section>
