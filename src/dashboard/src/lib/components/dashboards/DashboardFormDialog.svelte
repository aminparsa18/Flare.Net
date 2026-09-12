<script lang="ts">
	// Create/rename a dashboard - name+description only, same "bounded form fits Dialog's
	// modal-and-done shape" reasoning RenameViewDialog.svelte documents. Unlike that
	// dialog, this one also handles creation (`formTarget === 'new'`) - see
	// DashboardsState's own remarks for why a dashboard (unlike a SavedView) needs one.
	import { goto } from '$app/navigation';
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import { dashboardsContext } from '$lib/dashboards/context';
	import { dashboardPath } from '$lib/dashboards/page-paths';
	import * as m from '$lib/paraglide/messages';

	const dashboards = dashboardsContext.get();

	const open = $derived(dashboards.formTarget !== null);
	const isEdit = $derived(dashboards.formTarget !== null && dashboards.formTarget !== 'new');

	let name = $state('');
	let description = $state('');

	// Resets the draft whenever the dialog opens for a different target - same
	// "only reacts to identity change, not every keystroke" reasoning
	// RenameViewDialog.svelte's own effect documents.
	$effect(() => {
		const target = dashboards.formTarget;
		if (target === 'new') {
			name = '';
			description = '';
		} else if (target) {
			name = target.name;
			description = target.description;
		}
	});

	function handleOpenChange(next: boolean): void {
		if (!next) dashboards.closeForm();
	}

	async function handleSubmit(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		const wasNew = dashboards.formTarget === 'new';
		const id = await dashboards.save(name, description);
		// A brand-new dashboard has no panels yet - land straight on its (empty) viewer,
		// which is where the "pin a panel from Logs/Traces/Metrics" guidance lives, rather
		// than back on the list the user would just click through again.
		if (id && wasNew) {
			await goto(dashboardPath({ id }));
		}
	}
</script>

<Dialog.Root {open} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>{isEdit ? m.dashboardFormDialog_renameTitle() : m.dashboardFormDialog_createTitle()}</Dialog.Title>
			<Dialog.Description>
				{isEdit ? m.dashboardFormDialog_renameDescription() : m.dashboardFormDialog_createDescription()}
			</Dialog.Description>
		</Dialog.Header>
		<form class="space-y-4" onsubmit={handleSubmit}>
			<div class="space-y-2">
				<label for="dashboard-form-name" class="text-sm font-medium">{m.dashboardFormDialog_nameLabel()}</label>
				<Input id="dashboard-form-name" bind:value={name} required />
			</div>
			<div class="space-y-2">
				<label for="dashboard-form-description" class="text-sm font-medium">{m.dashboardFormDialog_descriptionLabel()}</label>
				<Textarea id="dashboard-form-description" bind:value={description} rows={2} />
			</div>
			{#if dashboards.saveError}
				<p class="text-destructive text-sm">{dashboards.saveError}</p>
			{/if}
			<Dialog.Footer>
				<Button type="button" variant="outline" onclick={() => dashboards.closeForm()}>{m.dashboardFormDialog_cancel()}</Button>
				<Button type="submit" disabled={dashboards.saving}>
					{#if dashboards.saving}<Spinner class="size-4" />{/if}
					{isEdit ? m.dashboardFormDialog_save() : m.dashboardFormDialog_create()}
				</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>
