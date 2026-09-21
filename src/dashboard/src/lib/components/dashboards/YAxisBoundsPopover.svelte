<script lang="ts">
	// Per-panel soft Y-axis min/max editor (roadmap's "Soft Y-axis min/max on metric charts"
	// item) - same "small icon-triggered popover with a mini form" shape as
	// PanelVariablesPopover.svelte/ApdexThresholdPopover.svelte, the closest existing
	// precedents for a per-row/per-panel settings affordance in this codebase. Metrics-only
	// (see DashboardPanelCard.svelte's own gating) - Logs/Traces panels chart through
	// VolumeChart, which has no configurable axis.
	//
	// *Soft*: either bound narrows the chart's default auto-ranged view, but never clips a
	// real point off it - MetricChart.svelte's `domainMin`/`domainMax` still expand past a
	// configured bound if the data actually goes further. `onApply(null, null)` (the Clear
	// button) restores that fully-auto default.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import MoveVerticalIcon from '@lucide/svelte/icons/move-vertical';
	import * as m from '$lib/paraglide/messages';

	let {
		yAxisMin,
		yAxisMax,
		onApply
	}: {
		yAxisMin: number | null | undefined;
		yAxisMax: number | null | undefined;
		onApply: (min: number | null, max: number | null) => void;
	} = $props();

	let open = $state(false);
	// Re-seeded from the latest saved values each time this is opened - see
	// ApdexThresholdPopover.svelte's identical `$effect` for why (referencing the props
	// directly here would only capture their initial value, not stay reactive to later
	// changes - svelte's `state_referenced_locally`).
	let minDraft = $state('');
	let maxDraft = $state('');
	let error = $state<string | null>(null);

	$effect(() => {
		if (open) {
			minDraft = yAxisMin != null ? String(yAxisMin) : '';
			maxDraft = yAxisMax != null ? String(yAxisMax) : '';
			error = null;
		}
	});

	const hasOverride = $derived(yAxisMin != null || yAxisMax != null);

	function apply(): void {
		const min = minDraft.trim() === '' ? null : Number(minDraft);
		const max = maxDraft.trim() === '' ? null : Number(maxDraft);
		if ((min != null && !Number.isFinite(min)) || (max != null && !Number.isFinite(max))) {
			error = m.yAxisBoundsPopover_invalidValue();
			return;
		}
		if (min != null && max != null && min >= max) {
			error = m.yAxisBoundsPopover_minNotLessThanMax();
			return;
		}
		onApply(min, max);
		open = false;
	}

	function clear(): void {
		onApply(null, null);
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
				title={m.yAxisBoundsPopover_title()}
			>
				<MoveVerticalIcon />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-64" align="end">
		<p class="mb-1 text-sm font-medium">{m.yAxisBoundsPopover_title()}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.yAxisBoundsPopover_description()}</p>
		<div class="space-y-2">
			<label class="flex items-center justify-between gap-2 text-xs">
				<span class="text-muted-foreground shrink-0">{m.yAxisBoundsPopover_min()}</span>
				<Input type="number" bind:value={minDraft} placeholder={m.yAxisBoundsPopover_auto()} class="h-8 w-28" />
			</label>
			<label class="flex items-center justify-between gap-2 text-xs">
				<span class="text-muted-foreground shrink-0">{m.yAxisBoundsPopover_max()}</span>
				<Input type="number" bind:value={maxDraft} placeholder={m.yAxisBoundsPopover_auto()} class="h-8 w-28" />
			</label>
		</div>
		{#if error}
			<p class="text-destructive mt-2 text-xs">{error}</p>
		{/if}
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={clear} disabled={!hasOverride}>{m.yAxisBoundsPopover_clear()}</Button>
			<Button size="sm" onclick={apply}>{m.yAxisBoundsPopover_apply()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
