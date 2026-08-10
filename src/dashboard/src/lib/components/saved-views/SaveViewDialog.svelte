<script lang="ts">
	// Small "save current view" form (name+description), opened from ViewsMenu.svelte's
	// "Save current view..." action. Deliberately a sibling of the Views popover, not
	// nested inside it - the popover closes itself before this opens (see ViewsMenu's
	// handleSaveClick), avoiding a Dialog-inside-Popover nesting/focus-trap footgun.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import { createSavedView, type PageType } from '$lib/saved-views-api';

	let {
		open = $bindable(false),
		pageType,
		currentState,
		onSaved
	}: {
		open: boolean;
		pageType: PageType;
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
			await createSavedView({ name, description, pageType, state: currentState() });
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
			<Dialog.Title>Save current view</Dialog.Title>
			<Dialog.Description>Saves this page's current filters so you (or anyone with the link) can reload them later.</Dialog.Description>
		</Dialog.Header>
		<form class="space-y-4" onsubmit={handleSubmit}>
			<div class="space-y-2">
				<label for="save-view-name" class="text-sm font-medium">Name</label>
				<Input id="save-view-name" bind:value={name} required placeholder="e.g. Errors, last hour" />
			</div>
			<div class="space-y-2">
				<label for="save-view-description" class="text-sm font-medium">Description</label>
				<Textarea id="save-view-description" bind:value={description} rows={2} />
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
