<script lang="ts">
	// Small "save current filter" form (name+description), opened from
	// SavedSearchesMenu.svelte's "Save current filter..." action. Deliberately a sibling
	// of the Saved searches popover, not nested inside it - the popover closes itself
	// before this opens (see SavedSearchesMenu's handleSaveClick), avoiding a
	// Dialog-inside-Popover nesting/focus-trap footgun. A Logs-only reskin of
	// saved-views/SaveViewDialog.svelte with pageType hardcoded to "Logs".
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import { createSavedView } from '$lib/saved-views-api';

	let {
		open = $bindable(false),
		currentState,
		onSaved
	}: {
		open: boolean;
		/** Called at submit time (not eagerly) so the saved state reflects whatever's current when the user actually clicks Save. */
		currentState: () => unknown;
		onSaved: () => void;
	} = $props();

	let name = $state('');
	let description = $state('');
	let saving = $state(false);
	let error = $state<string | null>(null);

	$effect(() => {
		if (open) {
			name = '';
			description = '';
			error = null;
		}
	});

	function handleOpenChange(next: boolean): void {
		if (!saving) open = next;
	}

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		saving = true;
		error = null;
		try {
			await createSavedView({ name, description, pageType: 'Logs', state: currentState() });
			open = false;
			onSaved();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}
</script>

<Dialog.Root {open} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>Save current filter</Dialog.Title>
			<Dialog.Description>Saves your current search, time range, and filters so you can re-run this search later.</Dialog.Description>
		</Dialog.Header>
		<form class="space-y-4" onsubmit={handleSubmit}>
			<div class="space-y-2">
				<label for="save-search-name" class="text-sm font-medium">Name</label>
				<Input id="save-search-name" bind:value={name} required placeholder="e.g. Production errors" />
			</div>
			<div class="space-y-2">
				<label for="save-search-description" class="text-sm font-medium">Description</label>
				<Textarea id="save-search-description" bind:value={description} rows={2} />
			</div>
			{#if error}
				<p class="text-destructive text-sm">{error}</p>
			{/if}
			<Dialog.Footer>
				<Button type="button" variant="outline" onclick={() => (open = false)}>Cancel</Button>
				<Button type="submit" disabled={saving}>
					{#if saving}<Spinner class="size-4" />{/if}
					Save
				</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>
