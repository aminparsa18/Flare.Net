<script lang="ts">
	// Viewer-header picker for a `multi` dashboard variable (see
	// docs-internal/adr/0058-multi-value-dashboard-variables.md) - a checkbox list instead of
	// the single-value `Select` every other variable gets, with an "All" row (clears the
	// selection) and a per-value "Only" shortcut (selects just that one). Selected values OR
	// together; an empty selection means "All", same as a single-value variable's "All".
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import { Input } from '$lib/components/ui/input';
	import type { DashboardVariable } from '$lib/dashboards-api';
	import SlidersHorizontalIcon from '@lucide/svelte/icons/sliders-horizontal';
	import * as m from '$lib/paraglide/messages';

	let {
		variable,
		options,
		selected,
		onChange
	}: {
		variable: DashboardVariable;
		options: string[];
		/** `[]` for "All". */
		selected: string[];
		onChange: (values: string[]) => void;
	} = $props();

	/** Past this many options the list gets a filter box - a Query variable resolves up to 50. */
	const FILTER_THRESHOLD = 8;

	let filter = $state('');

	/** Selected values stay listed even when a chained parent change has since dropped them
	 *  from `options`, so they can still be unchecked. */
	const allOptions = $derived([...options, ...selected.filter((v) => !options.includes(v))]);
	const visibleOptions = $derived.by(() => {
		const needle = filter.trim().toLowerCase();
		return needle ? allOptions.filter((o) => o.toLowerCase().includes(needle)) : allOptions;
	});

	const label = $derived.by(() => {
		if (selected.length === 0) return m.dashboardViewer_variableAll();
		if (selected.length === 1) return selected[0];
		return m.dashboardViewer_variableMore({ first: selected[0], count: selected.length - 1 });
	});

	function toggle(value: string, checked: boolean): void {
		onChange(checked ? [...selected, value] : selected.filter((v) => v !== value));
	}
</script>

<Popover.Root onOpenChange={(open) => !open && (filter = '')}>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="sm" class="max-w-72 font-normal" title={variable.name}>
				<SlidersHorizontalIcon data-icon="inline-start" />
				<span class="truncate">{variable.name}: {label}</span>
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-64 p-2" align="end">
		{#if allOptions.length > FILTER_THRESHOLD}
			<Input bind:value={filter} placeholder={m.dashboardViewer_variableFilterPlaceholder()} class="mb-2 h-8" />
		{/if}
		<label class="hover:bg-accent flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 text-sm">
			<Checkbox checked={selected.length === 0} onCheckedChange={(checked) => checked && onChange([])} />
			<span>{m.dashboardViewer_variableAll()}</span>
		</label>
		<div class="max-h-64 overflow-y-auto">
			{#each visibleOptions as option (option)}
				<div class="group hover:bg-accent flex items-center gap-2 rounded px-2 py-1.5 text-sm">
					<label class="flex min-w-0 flex-1 cursor-pointer items-center gap-2">
						<Checkbox checked={selected.includes(option)} onCheckedChange={(checked) => toggle(option, checked)} />
						<span class="truncate" title={option}>{option}</span>
					</label>
					<button
						type="button"
						class="text-muted-foreground hover:text-foreground shrink-0 text-xs opacity-0 group-hover:opacity-100 focus-visible:opacity-100"
						title={m.dashboardViewer_variableOnlyTitle()}
						onclick={() => onChange([option])}
					>
						{m.dashboardViewer_variableOnly()}
					</button>
				</div>
			{:else}
				<p class="text-muted-foreground px-2 py-1.5 text-sm">{m.dashboardViewer_variableNoOptions()}</p>
			{/each}
		</div>
	</Popover.Content>
</Popover.Root>
