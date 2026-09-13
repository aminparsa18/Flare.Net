<script lang="ts">
	// Lists a dashboard's variables (see docs-internal/adr/0025-dashboard-variables.md) with
	// edit/remove per row and an "Add variable" button - opened from the viewer's toolbar,
	// same "own dialog with its own open state, not tied to variableFormTarget" split
	// DashboardTable.svelte/DashboardFormDialog.svelte already establish for dashboards
	// themselves (this dialog is the list, VariableFormDialog.svelte - opened from here via
	// `viewer.openCreateVariable`/`openEditVariable` - is the add/edit form, stacked on top).
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import VariableFormDialog from './VariableFormDialog.svelte';
	import type { DashboardViewerState } from '$lib/dashboards/viewer.svelte';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import * as m from '$lib/paraglide/messages';

	let { open = $bindable(false), viewer }: { open: boolean; viewer: DashboardViewerState } = $props();

	const variables = $derived(viewer.variables);

	function targetSummary(v: (typeof variables)[number]): string {
		return v.target === 'Service' ? m.manageVariables_targetService() : m.manageVariables_targetAttribute({ bag: v.attributeBag ?? '', key: v.attributeKey ?? '' });
	}

	/** Name of the variable `v` chains off (see `DashboardVariable.dependsOnVariableId`), or
	 *  `null` if it's independent - shown as an extra badge so a chain is visible from the
	 *  list without opening each variable's own edit form. */
	function dependsOnName(v: (typeof variables)[number]): string | null {
		if (!v.dependsOnVariableId) return null;
		return variables.find((p) => p.id === v.dependsOnVariableId)?.name ?? null;
	}

	function handleRemove(id: string, name: string): void {
		if (!confirm(m.manageVariables_confirmRemove({ name }))) return;
		void viewer.removeVariable(id);
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{m.manageVariables_title()}</Dialog.Title>
			<Dialog.Description>{m.manageVariables_description()}</Dialog.Description>
		</Dialog.Header>

		<div class="space-y-2">
			{#if variables.length === 0}
				<p class="text-muted-foreground text-sm">{m.manageVariables_empty()}</p>
			{:else}
				{#each variables as variable (variable.id)}
					<div class="flex items-center justify-between gap-2 rounded-md border px-3 py-2">
						<div class="min-w-0">
							<div class="flex items-center gap-2">
								<span class="truncate text-sm font-medium">{variable.name}</span>
								<Badge variant="outline">{variable.sourceKind === 'Query' ? m.manageVariables_sourceQuery() : m.manageVariables_sourceCustom()}</Badge>
								{#if dependsOnName(variable)}
									<Badge variant="secondary">{m.manageVariables_dependsOn({ name: dependsOnName(variable) ?? '' })}</Badge>
								{/if}
							</div>
							<p class="text-muted-foreground truncate text-xs">{targetSummary(variable)}</p>
						</div>
						<div class="flex shrink-0 items-center gap-1">
							<Button variant="ghost" size="icon-sm" title={m.manageVariables_edit()} onclick={() => viewer.openEditVariable(variable)}>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-muted-foreground hover:text-destructive"
								title={m.manageVariables_remove()}
								onclick={() => handleRemove(variable.id, variable.name)}
							>
								<Trash2Icon />
							</Button>
						</div>
					</div>
				{/each}
			{/if}
		</div>

		<Dialog.Footer class="sm:justify-between">
			<Button variant="outline" size="sm" onclick={() => viewer.openCreateVariable()}>
				<PlusIcon data-icon="inline-start" />
				{m.manageVariables_add()}
			</Button>
			<Button variant="secondary" onclick={() => (open = false)}>{m.manageVariables_done()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<VariableFormDialog {viewer} />
