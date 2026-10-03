<script lang="ts">
	// Markdown editor for a `Text` panel - same icon-triggered popover shape as
	// PanelDescriptionPopover.svelte, edit-mode only (see DashboardPanelCard.svelte).
	import * as Popover from '$lib/components/ui/popover';
	import { Button } from '$lib/components/ui/button';
	import { Textarea } from '$lib/components/ui/textarea';
	import { TEXT_PANEL_MAX_LENGTH } from '$lib/dashboards/text-panel';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import * as m from '$lib/paraglide/messages';

	let {
		markdown,
		onApply
	}: {
		markdown: string;
		onApply: (markdown: string) => void;
	} = $props();

	let open = $state(false);
	let draft = $state('');

	// Re-seeded from the saved value each time this is opened - see YAxisBoundsPopover.svelte's identical `$effect`.
	$effect(() => {
		if (open) draft = markdown;
	});

	function apply(): void {
		onApply(draft);
		open = false;
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="ghost" size="icon-sm" class="text-muted-foreground hover:text-foreground shrink-0" title={m.textPanelEditPopover_title()}>
				<PencilIcon />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-96" align="end">
		<p class="mb-1 text-sm font-medium">{m.textPanelEditPopover_title()}</p>
		<p class="text-muted-foreground mb-3 text-xs">{m.textPanelEditPopover_description()}</p>
		<Textarea bind:value={draft} rows={10} maxlength={TEXT_PANEL_MAX_LENGTH} class="font-mono text-xs" aria-label={m.textPanelEditPopover_title()} />
		<div class="mt-3 flex justify-end">
			<Button size="sm" onclick={apply}>{m.textPanelEditPopover_apply()}</Button>
		</div>
	</Popover.Content>
</Popover.Root>
