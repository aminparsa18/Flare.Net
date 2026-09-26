<script lang="ts">
	// Per-column unit overrides for a Metrics panel's Table visualization
	// (`DashboardPanel.columnUnits`) - same icon-triggered mini-form shape as
	// YAxisBoundsPopover.svelte. One free-text UCUM unit per reducer column ("ms", "By",
	// "{request}/s" - whatever `resolveAxisScale` understands, anything else shown as a literal
	// suffix); blank keeps the metric's own unit. The datalist only suggests common units.
	// Only rendered by DashboardPanelCard.svelte while `editing` a Table-visualization panel.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { parseColumnUnits, type PanelReducer } from '$lib/dashboards/visualization';
	import { reducerLabel } from './panels/visualizations/labels';
	import RulerIcon from '@lucide/svelte/icons/ruler';
	import * as m from '$lib/paraglide/messages';

	let {
		columnUnits,
		onApply
	}: {
		/** The panel's stored `columnUnits`, unvalidated. */
		columnUnits: unknown;
		onApply: (columnUnits: Partial<Record<PanelReducer, string>>) => void;
	} = $props();

	// TableVisualization's own column order, not PANEL_REDUCERS' (the reducer menu's).
	const COLUMNS: readonly PanelReducer[] = ['last', 'min', 'avg', 'max', 'sum'];
	const SUGGESTIONS = ['ms', 's', 'µs', 'ns', 'By', 'KiBy', 'MiBy', 'GiBy', '%', '1', '{request}', '{request}/s', 'By/s'];
	const datalistId = $props.id();

	let open = $state(false);
	let drafts = $state<Record<PanelReducer, string>>({ last: '', avg: '', sum: '', min: '', max: '' });

	const saved = $derived(parseColumnUnits(columnUnits));
	const hasOverride = $derived(Object.keys(saved).length > 0);

	// Re-seeded from the saved values each time this opens - see YAxisBoundsPopover.svelte.
	$effect(() => {
		if (open) {
			drafts = { last: saved.last ?? '', avg: saved.avg ?? '', sum: saved.sum ?? '', min: saved.min ?? '', max: saved.max ?? '' };
		}
	});

	function apply(): void {
		onApply(parseColumnUnits(drafts));
		open = false;
	}

	function clear(): void {
		onApply({});
		open = false;
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button
				{...props}
				variant="ghost"
				size="icon-sm"
				class={hasOverride ? 'text-foreground shrink-0' : 'text-muted-foreground hover:text-foreground shrink-0'}
				title={m.columnUnitsPopover_title()}
			>
				<RulerIcon />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-64" align="end">
		<p class="mb-1 text-sm font-medium">{m.columnUnitsPopover_title()}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.columnUnitsPopover_description()}</p>
		<datalist id={datalistId}>
			{#each SUGGESTIONS as unit (unit)}
				<option value={unit}></option>
			{/each}
		</datalist>
		<div class="space-y-2">
			{#each COLUMNS as column (column)}
				<label class="flex items-center justify-between gap-2 text-xs">
					<span class="text-muted-foreground shrink-0">{reducerLabel(column)}</span>
					<Input bind:value={drafts[column]} list={datalistId} placeholder={m.columnUnitsPopover_metricUnit()} class="h-8 w-32" />
				</label>
			{/each}
		</div>
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={clear} disabled={!hasOverride}>{m.columnUnitsPopover_clear()}</Button>
			<Button size="sm" onclick={apply}>{m.columnUnitsPopover_apply()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
