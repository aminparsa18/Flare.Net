<script lang="ts">
	// "Saved searches" toolbar control for the Logs page - lists saved searches (load
	// current filter), a "Save current filter..." action (opens SaveSearchDialog), and
	// inline delete per row. A Logs-only reskin of the shared saved-views/ViewsMenu.svelte
	// (same underlying /api/views data, pageType "Logs" hardcoded) - Traces/Metrics keep
	// using the generic ViewsMenu unchanged.
	import * as Popover from '$lib/components/ui/popover';
	import * as Command from '$lib/components/ui/command';
	import { Button } from '$lib/components/ui/button';
	import { ScrollArea } from '$lib/components/ui/scroll-area';
	import { Spinner } from '$lib/components/ui/spinner';
	import SaveSearchDialog from './SaveSearchDialog.svelte';
	import { listSavedViews, deleteSavedView, type SavedView } from '$lib/saved-views-api';
	import StarIcon from '@lucide/svelte/icons/star';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import SaveIcon from '@lucide/svelte/icons/save';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';

	let {
		currentState,
		applyState
	}: {
		currentState: () => unknown;
		applyState: (state: unknown) => void | Promise<void>;
	} = $props();

	let open = $state(false);
	let views = $state<SavedView[]>([]);
	let loading = $state(false);
	let saveDialogOpen = $state(false);
	let deletingId = $state<string | null>(null);
	let deleteError = $state<string | null>(null);

	async function loadViews(): Promise<void> {
		loading = true;
		try {
			const res = await listSavedViews('Logs');
			views = res.views;
		} catch {
			views = []; // non-critical - the picker just shows empty until reopened
		} finally {
			loading = false;
		}
	}

	// Fetches fresh every time the popover opens (cheap, low-cardinality list) rather than
	// once at mount, so a search saved via SaveSearchDialog (or deleted from /views in
	// another tab) shows up next time this reopens without extra plumbing.
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

	async function handleDelete(view: SavedView): Promise<void> {
		if (!confirm(`Delete saved search "${view.name}"? This cannot be undone.`)) return;
		deletingId = view.id;
		deleteError = null;
		try {
			await deleteSavedView(view.id);
			views = views.filter((v) => v.id !== view.id); // local update - no refetch/close needed
		} catch (err) {
			deleteError = err instanceof Error ? err.message : String(err);
		} finally {
			deletingId = null;
		}
	}
</script>

<Popover.Root bind:open>
	<Popover.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="sm">
				<StarIcon data-icon="inline-start" />
				Saved searches
				<ChevronDownIcon data-icon="inline-end" />
			</Button>
		{/snippet}
	</Popover.Trigger>
	<Popover.Content class="w-64 p-0" align="start">
		<Command.Root>
			<Command.List>
				<Command.Group heading="My saved searches">
					{#if loading}
						<div class="text-muted-foreground px-2 py-4 text-center text-xs">Loading…</div>
					{:else if views.length === 0}
						<div class="text-muted-foreground flex flex-col items-center gap-1 px-2 py-6 text-center">
							<StarIcon class="size-5 opacity-50" />
							<p class="text-xs">No saved searches yet</p>
							<p class="text-xs">Save your current filters to re-run this search later.</p>
						</div>
					{:else}
						<ScrollArea class="max-h-64">
							{#each views as view (view.id)}
								<div class="group/row flex items-center">
									<Command.Item value={view.name} onSelect={() => handleSelect(view)} class="min-w-0 flex-1">
										<span class="truncate">{view.name}</span>
									</Command.Item>
									<Button
										variant="ghost"
										size="icon-sm"
										class="text-muted-foreground hover:text-destructive mr-1 shrink-0"
										title="Delete saved search"
										disabled={deletingId === view.id}
										onclick={(e) => {
											e.stopPropagation();
											handleDelete(view);
										}}
									>
										{#if deletingId === view.id}
											<Spinner class="size-3" />
										{:else}
											<Trash2Icon />
										{/if}
									</Button>
								</div>
							{/each}
						</ScrollArea>
					{/if}
				</Command.Group>
			</Command.List>
		</Command.Root>
		{#if deleteError}
			<p class="text-destructive px-2 py-1 text-xs">{deleteError}</p>
		{/if}
		<div class="border-t p-1">
			<Button variant="ghost" size="sm" class="w-full justify-start" onclick={handleSaveClick}>
				<SaveIcon data-icon="inline-start" />
				Save current filter…
			</Button>
		</div>
	</Popover.Content>
</Popover.Root>

<SaveSearchDialog bind:open={saveDialogOpen} {currentState} onSaved={loadViews} />
