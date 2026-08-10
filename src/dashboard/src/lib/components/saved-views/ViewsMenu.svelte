<script lang="ts">
	// "Views" toolbar control shared by LogsToolbar/TracesToolbar/MetricsToolbar - lists
	// existing saved views for this page (load current filter), and a "Save current
	// view..." action (opens SaveViewDialog). Generic over which explorer it's wired to
	// via `currentState`/`applyState`, since Logs/Traces/Metrics each have their own
	// state-class shape and (for Metrics) an async applySavedViewState.
	import * as Popover from '$lib/components/ui/popover';
	import * as Command from '$lib/components/ui/command';
	import { Button } from '$lib/components/ui/button';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import SaveViewDialog from './SaveViewDialog.svelte';
	import { listSavedViews, type PageType, type SavedView } from '$lib/saved-views-api';
	import LayoutGridIcon from '@lucide/svelte/icons/layout-grid';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import SaveIcon from '@lucide/svelte/icons/save';

	let {
		pageType,
		currentState,
		applyState
	}: {
		pageType: PageType;
		currentState: () => unknown;
		applyState: (state: unknown) => void | Promise<void>;
	} = $props();

	let open = $state(false);
	let views = $state<SavedView[]>([]);
	let loading = $state(false);
	let saveDialogOpen = $state(false);

	async function loadViews(): Promise<void> {
		loading = true;
		try {
			const res = await listSavedViews(pageType);
			views = res.views;
		} catch {
			views = []; // non-critical - the picker just shows empty until reopened
		} finally {
			loading = false;
		}
	}

	// Fetches fresh every time the popover opens (cheap, low-cardinality list) rather than
	// once at mount, so a view saved via SaveViewDialog (or deleted from /views in another
	// tab) shows up next time this reopens without extra plumbing.
	$effect(() => {
		if (open) void loadViews();
	});

	async function handleSelect(view: SavedView): Promise<void> {
		open = false;
		await applyState(view.state);
	}

	function handleSaveClick(): void {
		open = false;
		saveDialogOpen = true;
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="sm">
				<LayoutGridIcon data-icon="inline-start" />
				Views
				<ChevronDownIcon data-icon="inline-end" />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-64 p-0" align="start">
		<Command.Root>
			<Command.List>
				<Command.Group heading="Saved views">
					{#if loading}
						<div class="text-muted-foreground px-2 py-4 text-center text-xs">Loading…</div>
					{:else if views.length === 0}
						<div class="text-muted-foreground px-2 py-4 text-center text-xs">No saved views for this page yet.</div>
					{:else}
						<ScrollArea class="max-h-64">
							{#each views as view (view.id)}
								<Command.Item value={view.name} onSelect={() => handleSelect(view)}>
									{view.name}
								</Command.Item>
							{/each}
						</ScrollArea>
					{/if}
				</Command.Group>
			</Command.List>
		</Command.Root>
		<div class="border-t p-1">
			<Button variant="ghost" size="sm" class="w-full justify-start" onclick={handleSaveClick}>
				<SaveIcon data-icon="inline-start" />
				Save current view…
			</Button>
		</div>
	</Popover.Content>
</Popover.Root>

<SaveViewDialog bind:open={saveDialogOpen} {pageType} {currentState} onSaved={loadViews} />
