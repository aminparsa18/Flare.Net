<script lang="ts">
	// Per-panel "Visualization" menu (roadmap's "Dashboard panel visualization types" item) -
	// switches a Metrics panel between line/bar/value/pie/table/histogram/heatmap in place, keeping
	// its query, plus the reducer the single-number visualizations collapse each series with.
	// Only rendered by DashboardPanelCard.svelte for Metrics panels while `editing`, same
	// gating as YAxisBoundsPopover/ThresholdsPopover. See
	// docs-internal/adr/0059-dashboard-panel-visualizations.md.
	import * as DropdownMenu from '$lib/components/ui/dropdown-menu';
	import { Button } from '$lib/components/ui/button';
	import {
		PANEL_REDUCERS,
		PANEL_STACKINGS,
		PANEL_VISUALIZATIONS,
		usesReducer,
		usesStacking,
		type PanelReducer,
		type PanelStacking,
		type PanelVisualization
	} from '$lib/dashboards/visualization';
	import { reducerLabel, stackingLabel, visualizationLabel } from './panels/visualizations/labels';
	import ChartLineIcon from '@lucide/svelte/icons/chart-line';
	import ChartColumnIcon from '@lucide/svelte/icons/chart-column';
		import HashIcon from '@lucide/svelte/icons/hash';
	import ChartPieIcon from '@lucide/svelte/icons/chart-pie';
	import TableIcon from '@lucide/svelte/icons/table';
	import HistogramIcon from '@lucide/svelte/icons/chart-no-axes-column';
	import HeatmapIcon from '@lucide/svelte/icons/grid-3x3';
	import * as m from '$lib/paraglide/messages';

	let {
		visualization,
		stacking,
		reducer,
		onChange,
		onStackingChange
	}: {
		visualization: PanelVisualization;
		/** The panel's effective stacking (`parseStacking`) - only the bar chart shows the choice. */
		stacking: PanelStacking;
		/** The panel's stored reducer, or `null` for "the result type's default". */
		reducer: PanelReducer | null;
		onChange: (visualization: PanelVisualization, reducer: PanelReducer | null) => void;
		onStackingChange: (stacking: PanelStacking) => void;
	} = $props();

	const ICONS = {
		timeSeries: ChartLineIcon,
		bar: ChartColumnIcon,
		value: HashIcon,
		pie: ChartPieIcon,
		table: TableIcon,
		histogram: HistogramIcon,
		heatmap: HeatmapIcon
	} as const;

	// bits-ui's RadioGroup value is a string, so "default reducer" (`null`) needs a sentinel.
	const AUTO = '__auto__';

	const TriggerIcon = $derived(ICONS[visualization]);
</script>

<DropdownMenu.Root>
	<DropdownMenu.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="ghost" size="icon-sm" class="text-muted-foreground hover:text-foreground shrink-0" title={m.panelVisualization_menu()}>
				<TriggerIcon />
			</Button>
		{/snippet}
	</DropdownMenu.Trigger>
	<DropdownMenu.Content class="w-52" align="end">
		<DropdownMenu.Label>{m.panelVisualization_menu()}</DropdownMenu.Label>
		<DropdownMenu.RadioGroup value={visualization} onValueChange={(v) => v && onChange(v as PanelVisualization, reducer)}>
			{#each PANEL_VISUALIZATIONS as option (option)}
				{@const Icon = ICONS[option]}
				<DropdownMenu.RadioItem value={option}>
					<Icon class="text-muted-foreground size-4" />
					{visualizationLabel(option)}
				</DropdownMenu.RadioItem>
			{/each}
		</DropdownMenu.RadioGroup>
		{#if usesStacking(visualization)}
			<DropdownMenu.Separator />
			<DropdownMenu.Label>{m.panelVisualization_stacking()}</DropdownMenu.Label>
			<DropdownMenu.RadioGroup value={stacking} onValueChange={(v) => v && onStackingChange(v as PanelStacking)}>
				{#each PANEL_STACKINGS as option (option)}
					<DropdownMenu.RadioItem value={option}>{stackingLabel(option)}</DropdownMenu.RadioItem>
				{/each}
			</DropdownMenu.RadioGroup>
		{/if}
		{#if usesReducer(visualization)}
			<DropdownMenu.Separator />
			<DropdownMenu.Label>{m.panelVisualization_reducer()}</DropdownMenu.Label>
			<DropdownMenu.RadioGroup value={reducer ?? AUTO} onValueChange={(v) => v && onChange(visualization, v === AUTO ? null : (v as PanelReducer))}>
				<DropdownMenu.RadioItem value={AUTO}>{m.panelVisualization_reducerAuto()}</DropdownMenu.RadioItem>
				{#each PANEL_REDUCERS as option (option)}
					<DropdownMenu.RadioItem value={option}>{reducerLabel(option)}</DropdownMenu.RadioItem>
				{/each}
			</DropdownMenu.RadioGroup>
		{/if}
	</DropdownMenu.Content>
</DropdownMenu.Root>
