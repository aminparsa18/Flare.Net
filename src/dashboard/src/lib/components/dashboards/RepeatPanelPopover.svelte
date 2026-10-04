<script lang="ts">
	// "Repeat for variable" option for a panel (`DashboardPanel.repeatVariableId`/
	// `repeatDirection`, see `repeatValues` in `$lib/dashboards/variables.ts`). Only multi-value
	// variables are offered - a single-value variable has only one value to repeat over. Applied
	// immediately on change, like PanelVariablesPopover. Rendered by DashboardPanelCard.svelte
	// while `editing`.
	import * as Popover from '$lib/components/ui/popover';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import type { DashboardVariable } from '$lib/dashboards-api';
	import Repeat2Icon from '@lucide/svelte/icons/repeat-2';
	import * as m from '$lib/paraglide/messages';

	let {
		variables,
		repeatVariableId,
		repeatDirection,
		onChange
	}: {
		variables: DashboardVariable[];
		repeatVariableId: string | undefined;
		repeatDirection: 'horizontal' | 'vertical' | undefined;
		onChange: (variableId: string | null, direction: 'horizontal' | 'vertical') => void;
	} = $props();

	/** Select value - bits-ui treats `''` as "nothing selected". */
	const NONE = '__none__';
	const multiVariables = $derived(variables.filter((v) => v.multi));
	const active = $derived(multiVariables.find((v) => v.id === repeatVariableId));
	const direction = $derived(repeatDirection ?? 'horizontal');
</script>

<Popover.Root>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button
				{...props}
				variant="ghost"
				size="icon-sm"
				class={active ? 'text-foreground shrink-0' : 'text-muted-foreground hover:text-foreground shrink-0'}
				title={active ? m.repeatPopover_titleActive({ name: active.name }) : m.repeatPopover_title()}
			>
				<Repeat2Icon />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-72" align="start">
		<p class="mb-2 text-sm font-medium">{m.repeatPopover_title()}</p>
		{#if multiVariables.length === 0}
			<p class="text-muted-foreground text-xs">{m.repeatPopover_noMulti()}</p>
		{:else}
			<p class="text-muted-foreground mb-3 text-xs">{m.repeatPopover_description()}</p>
			<div class="space-y-2">
				<label class="flex items-center justify-between gap-2 text-xs">
					<span class="text-muted-foreground">{m.repeatPopover_variable()}</span>
					<Select.Root type="single" value={active?.id ?? NONE} onValueChange={(v) => onChange(v === NONE ? null : v, direction)}>
						<Select.Trigger class="h-8 w-40">{active?.name ?? m.repeatPopover_none()}</Select.Trigger>
						<Select.Content>
							<Select.Item value={NONE} label={m.repeatPopover_none()} />
							{#each multiVariables as variable (variable.id)}
								<Select.Item value={variable.id} label={variable.name} />
							{/each}
						</Select.Content>
					</Select.Root>
				</label>
				{#if active}
					<label class="flex items-center justify-between gap-2 text-xs">
						<span class="text-muted-foreground">{m.repeatPopover_direction()}</span>
						<Select.Root type="single" value={direction} onValueChange={(v) => onChange(active.id, v as 'horizontal' | 'vertical')}>
							<Select.Trigger class="h-8 w-40">{direction === 'vertical' ? m.repeatPopover_vertical() : m.repeatPopover_horizontal()}</Select.Trigger>
							<Select.Content>
								<Select.Item value="horizontal" label={m.repeatPopover_horizontal()} />
								<Select.Item value="vertical" label={m.repeatPopover_vertical()} />
							</Select.Content>
						</Select.Root>
					</label>
				{/if}
			</div>
		{/if}
	</Popover.Content>
</Popover.Root>
