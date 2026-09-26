<script lang="ts">
	// Table visualization: one row per series, one column per reducer (Last/Min/Avg/Max, plus
	// Sum for a Sum metric - summing Gauge levels or Histogram means has no meaning). Click a
	// header to sort by it (again to flip direction); the panel's own reducer column is the
	// initial sort, largest first. The search box filters rows by series label; the download
	// button exports exactly what's shown (filtered + sorted) as CSV with raw, unscaled
	// values. Cells matching a threshold rule take its color. Sort/search are session-only
	// view state, never saved to the panel. A column's unit is the metric's own unless the
	// panel's `columnUnits` overrides it (ColumnUnitsPopover.svelte) - e.g. giving a
	// unit-less formula result "ms", or reading a Sum column as bytes.
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { matchThreshold, thresholdColorValue, type PanelThreshold } from '$lib/dashboards/thresholds';
	import { formatValue, reduceValues, toCsv, type PanelReducer, type VizSeries } from '$lib/dashboards/visualization';
	import { downloadBlob } from '$lib/logs/export';
	import { slugify } from '$lib/dashboards/state.svelte';
	import { reducerLabel } from './labels';
	import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
	import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
	import DownloadIcon from '@lucide/svelte/icons/file-down';
	import SearchIcon from '@lucide/svelte/icons/search';
	import * as m from '$lib/paraglide/messages';

	let {
		series,
		unit,
		columnUnits = {},
		reducer,
		includeSum,
		title,
		thresholds = []
	}: {
		series: VizSeries[];
		unit: string | null;
		/** Already-parsed per-column overrides (`parseColumnUnits`); a missing key uses `unit`. */
		columnUnits?: Partial<Record<PanelReducer, string>>;
		reducer: PanelReducer;
		/** Whether a Sum column makes sense for this data (a Sum metric's increments) - see the header comment. */
		includeSum: boolean;
		/** The panel's title - names the downloaded CSV file. */
		title: string;
		thresholds?: PanelThreshold[];
	} = $props();

	type SortKey = 'label' | PanelReducer;

	const columns = $derived<PanelReducer[]>(includeSum ? ['last', 'min', 'avg', 'max', 'sum'] : ['last', 'min', 'avg', 'max']);

	// `undefined` until the user clicks a header - until then the sort follows the panel's
	// reducer, so switching the reducer re-sorts rather than being pinned to a stale column.
	let userSortKey = $state<SortKey | undefined>(undefined);
	let sortDescending = $state(true);
	const sortKey = $derived<SortKey>(userSortKey ?? (columns.includes(reducer) ? reducer : 'avg'));
	let search = $state('');

	const rows = $derived(
		series.map((s) => {
			const vals = s.points.map((p) => p.value);
			const stats = {} as Record<PanelReducer, number | null>;
			for (const r of ['last', 'min', 'avg', 'max', 'sum'] as const) stats[r] = reduceValues(vals, r);
			return { label: s.displayLabel, title: s.label, stats };
		})
	);

	const shown = $derived.by(() => {
		const needle = search.trim().toLowerCase();
		const filtered = needle ? rows.filter((r) => r.label.toLowerCase().includes(needle)) : rows;
		const dir = sortDescending ? -1 : 1;
		return [...filtered].sort((a, b) => {
			if (sortKey === 'label') return dir * a.label.localeCompare(b.label);
			const av = a.stats[sortKey];
			const bv = b.stats[sortKey];
			// Rows with no value sink to the bottom in either direction.
			if (av == null) return bv == null ? 0 : 1;
			if (bv == null) return -1;
			return dir * (av - bv);
		});
	});

	function toggleSort(key: SortKey): void {
		if (sortKey === key) {
			sortDescending = !sortDescending;
		} else {
			userSortKey = key;
			// Text reads naturally A-Z; numbers are usually wanted largest first.
			sortDescending = key !== 'label';
		}
	}

	function downloadCsv(): void {
		const header = [m.panelVisualization_tableSeries(), ...columns.map(reducerLabel)];
		const body = shown.map((r) => [r.label, ...columns.map((c) => r.stats[c])]);
		const blob = new Blob([toCsv(header, body)], { type: 'text/csv;charset=utf-8' });
		downloadBlob(blob, `${slugify(title) || 'panel'}.csv`);
	}
</script>

<div class="flex min-h-0 flex-1 flex-col gap-2 p-2">
	<div class="flex shrink-0 items-center gap-2">
		<div class="relative min-w-0 flex-1">
			<SearchIcon class="text-muted-foreground pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2" />
			<Input class="h-7 pl-7 text-xs" placeholder={m.panelVisualization_tableSearch()} aria-label={m.panelVisualization_tableSearch()} bind:value={search} />
		</div>
		<Button variant="ghost" size="icon-sm" class="text-muted-foreground hover:text-foreground shrink-0" title={m.panelVisualization_tableDownloadCsv()} onclick={downloadCsv}>
			<DownloadIcon />
		</Button>
	</div>
	<div class="min-h-0 flex-1 overflow-auto">
		<table class="w-full text-xs">
			<thead class="bg-background sticky top-0">
				<tr class="border-b">
					{#each ['label', ...columns] as SortKey[] as key (key)}
						<th class="px-2 py-1.5 font-medium {key === 'label' ? 'w-2/5 text-left' : 'text-right'}" aria-sort={sortKey === key ? (sortDescending ? 'descending' : 'ascending') : 'none'}>
							<button type="button" class="hover:text-foreground text-muted-foreground inline-flex items-center gap-1 {sortKey === key ? 'text-foreground' : ''}" onclick={() => toggleSort(key)}>
								{key === 'label' ? m.panelVisualization_tableSeries() : reducerLabel(key)}
								{#if sortKey === key}
									{#if sortDescending}<ArrowDownIcon class="size-3" />{:else}<ArrowUpIcon class="size-3" />{/if}
								{/if}
							</button>
						</th>
					{/each}
				</tr>
			</thead>
			<tbody>
				{#each shown as row (row.label)}
					<tr class="hover:bg-muted/50 border-b last:border-b-0">
						<td class="max-w-0 truncate px-2 py-1.5" title={row.title}>{row.label}</td>
						{#each columns as c (c)}
							{@const v = row.stats[c]}
							{@const match = v == null ? undefined : matchThreshold(thresholds, v)}
							<td
								class="px-2 py-1.5 text-right whitespace-nowrap tabular-nums {c === reducer ? 'font-medium' : ''}"
								style={match ? `color: ${thresholdColorValue(match.color)};` : undefined}
								title={v == null ? undefined : String(v)}
							>
								{v == null ? '-' : formatValue(v, columnUnits[c] ?? unit)}
							</td>
						{/each}
					</tr>
				{:else}
					<tr><td colspan={columns.length + 1} class="text-muted-foreground px-2 py-4 text-center">{m.panelVisualization_tableNoMatches()}</td></tr>
				{/each}
			</tbody>
		</table>
	</div>
</div>
