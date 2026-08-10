<script lang="ts">
	// Rename/redescribe a saved view - name+description only, no filter-state editing
	// here (that lives on the source explorer page, not this management page). Mirrors
	// AlertRuleFormDialog's "bounded form fits Dialog's modal-and-done shape" reasoning,
	// just a smaller form.
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import { savedViewsContext } from '$lib/saved-views/context';

	const views = savedViewsContext.get();

	const open = $derived(views.renameTarget !== null);

	let name = $state('');
	let description = $state('');

	// Resets the draft whenever the dialog opens for a different target - same
	// "only reacts to identity change, not every keystroke" reasoning
	// AlertRuleFormDialog's own effect documents.
	$effect(() => {
		const target = views.renameTarget;
		if (target) {
			name = target.name;
			description = target.description;
		}
	});

	function handleOpenChange(next: boolean): void {
		if (!next) views.closeRename();
	}

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		await views.rename(name, description);
	}
</script>

<Dialog.Root {open} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>Rename view</Dialog.Title>
			<Dialog.Description>Update this saved view's name or description. Its filter is unchanged.</Dialog.Description>
		</Dialog.Header>
		<form class="space-y-4" onsubmit={handleSubmit}>
			<div class="space-y-2">
				<label for="rename-view-name" class="text-sm font-medium">Name</label>
				<Input id="rename-view-name" bind:value={name} required />
			</div>
			<div class="space-y-2">
				<label for="rename-view-description" class="text-sm font-medium">Description</label>
				<Textarea id="rename-view-description" bind:value={description} rows={2} />
			</div>
			{#if views.saveError}
				<p class="text-destructive text-sm">{views.saveError}</p>
			{/if}
			<Dialog.Footer>
				<Button type="button" variant="outline" onclick={() => views.closeRename()}>Cancel</Button>
				<Button type="submit" disabled={views.saving}>
					{#if views.saving}<Spinner class="size-4" />{/if}
					Save
				</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>
