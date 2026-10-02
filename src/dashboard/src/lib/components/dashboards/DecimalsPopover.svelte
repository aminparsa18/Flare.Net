<script lang="ts">
	// Per-panel decimal precision (`DashboardPanel.decimals`) for a Metrics panel - same
	// icon-triggered mini-form shape as ColumnUnitsPopover.svelte. Blank keeps auto
	// (magnitude-based) formatting; 0-6 pins the fraction digits everywhere the panel
	// formats a value. Only rendered by DashboardPanelCard.svelte while `editing`.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { parseDecimals } from '$lib/metrics/axis';
	import HashIcon from '@lucide/svelte/icons/hash';
	import * as m from '$lib/paraglide/messages';

	let {
		decimals,
		onApply
	}: {
		/** The panel's stored `decimals`, unvalidated. */
		decimals: unknown;
		onApply: (decimals: number | undefined) => void;
	} = $props();

	let open = $state(false);
	let draft = $state('');

	const saved = $derived(parseDecimals(decimals));

	// Re-seeded from the saved value each time this opens - see YAxisBoundsPopover.svelte.
	$effect(() => {
		if (open) draft = saved !== undefined ? String(saved) : '';
	});

	const draftValue = $derived(draft.trim() === '' ? undefined : parseDecimals(Number(draft)));
	const valid = $derived(draft.trim() === '' || draftValue !== undefined);

	function apply(): void {
		if (!valid) return;
		onApply(draftValue);
		open = false;
	}

	function clear(): void {
		onApply(undefined);
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
				class={saved !== undefined ? 'text-foreground shrink-0' : 'text-muted-foreground hover:text-foreground shrink-0'}
				title={m.decimalsPopover_title()}
			>
				<HashIcon />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-64" align="end">
		<p class="mb-1 text-sm font-medium">{m.decimalsPopover_title()}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.decimalsPopover_description()}</p>
		<Input inputmode="numeric" maxlength={1} bind:value={draft} placeholder={m.decimalsPopover_auto()} aria-invalid={!valid} class="h-8" />
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={clear} disabled={saved === undefined}>{m.decimalsPopover_clear()}</Button>
			<Button size="sm" onclick={apply} disabled={!valid}>{m.decimalsPopover_apply()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
