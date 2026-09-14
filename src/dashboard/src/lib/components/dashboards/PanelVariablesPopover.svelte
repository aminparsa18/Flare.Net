<script lang="ts">
	// Per-panel opt-out from a dashboard variable (roadmap's "Per-panel opt-out from a
	// dashboard variable" item, following Phase 5's variables/chaining -
	// docs-internal/adr/0025-dashboard-variables.md). Every dashboard variable narrows every
	// applicable panel by default; this popover lets one panel say "don't narrow me" for
	// individual variables via `DashboardPanel.excludedVariableIds`, without touching the
	// variable's definition or any other panel. Only rendered by DashboardPanelCard.svelte
	// while `editing` and `variables.length > 0` - a variable-less dashboard, or a read-only
	// viewer, has nothing here to opt out of.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import type { DashboardVariable } from '$lib/dashboards-api';
	import FilterIcon from '@lucide/svelte/icons/filter';
	import * as m from '$lib/paraglide/messages';

	let {
		variables,
		excludedVariableIds,
		onToggle
	}: {
		variables: DashboardVariable[];
		/** This panel's own `DashboardPanel.excludedVariableIds`, or `undefined`/`[]` for "narrowed by every variable" (the default). */
		excludedVariableIds: string[] | undefined;
		onToggle: (variableId: string, excluded: boolean) => void;
	} = $props();

	const excludedCount = $derived(excludedVariableIds?.length ?? 0);
	const buttonLabel = $derived(excludedCount > 0 ? m.panelVariablesPopover_titleWithCount({ count: excludedCount }) : m.panelVariablesPopover_title());
</script>

<Popover.Root>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button
				{...props}
				variant="ghost"
				size="icon-sm"
				class={excludedCount > 0 ? 'text-foreground shrink-0' : 'text-muted-foreground hover:text-foreground shrink-0'}
				title={buttonLabel}
			>
				<FilterIcon />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-64" align="start">
		<p class="mb-2 text-sm font-medium">{m.panelVariablesPopover_title()}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.panelVariablesPopover_description()}</p>
		<div class="space-y-2">
			{#each variables as variable (variable.id)}
				{@const excluded = excludedVariableIds?.includes(variable.id) ?? false}
				<label class="flex cursor-pointer items-center gap-2 text-sm">
					<Checkbox checked={!excluded} onCheckedChange={(checked) => onToggle(variable.id, !checked)} />
					<span class="truncate">{variable.name}</span>
				</label>
			{/each}
		</div>
	</Popover.Content>
</Popover.Root>
