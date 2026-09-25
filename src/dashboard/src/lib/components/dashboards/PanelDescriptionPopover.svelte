<script lang="ts">
	// Per-panel description editor (roadmap's "Per-panel descriptions on dashboards" item) -
	// same "small icon-triggered popover with a mini form" shape as YAxisBoundsPopover.svelte.
	// Edit-mode only (see DashboardPanelCard.svelte's own gating); the description itself is
	// shown in both modes, as the info icon next to the panel title.
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Textarea } from '$lib/components/ui/textarea';
	import NotebookPenIcon from '@lucide/svelte/icons/notebook-pen';
	import * as m from '$lib/paraglide/messages';

	let {
		description,
		onApply
	}: {
		description: string | undefined;
		onApply: (description: string) => void;
	} = $props();

	let open = $state(false);
	let draft = $state('');

	// Re-seeded from the saved value each time this is opened - see YAxisBoundsPopover.svelte's
	// identical `$effect` for why.
	$effect(() => {
		if (open) draft = description ?? '';
	});

	const hasDescription = $derived(!!description);

	function apply(): void {
		onApply(draft);
		open = false;
	}

	function clear(): void {
		onApply('');
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
				class={hasDescription ? 'text-foreground shrink-0' : 'text-muted-foreground hover:text-foreground shrink-0'}
				title={m.panelDescriptionPopover_title()}
			>
				<NotebookPenIcon />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-72" align="end">
		<p class="mb-1 text-sm font-medium">{m.panelDescriptionPopover_title()}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.panelDescriptionPopover_description()}</p>
		<Textarea bind:value={draft} rows={4} maxlength={1000} aria-label={m.panelDescriptionPopover_title()} />
		<div class="mt-3 flex justify-between gap-2">
			<Button variant="ghost" size="sm" onclick={clear} disabled={!hasDescription}>{m.panelDescriptionPopover_clear()}</Button>
			<Button size="sm" onclick={apply}>{m.panelDescriptionPopover_apply()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
