<script lang="ts">
	// Settings > Notification templates (ADR-0148): named, reusable alert wording that rules pick
	// by reference. Local state rather than a context - nothing else on this page shares it.
	import { onMount } from 'svelte';
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import AlertTemplateFormDialog from '$lib/components/alert-templates/AlertTemplateFormDialog.svelte';
	import {
		listAlertTemplates,
		createAlertTemplate,
		updateAlertTemplate,
		deleteAlertTemplate,
		type AlertTemplate,
		type AlertTemplateRequest
	} from '$lib/alert-templates-api';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import FileTextIcon from '@lucide/svelte/icons/file-text';

	let templates = $state.raw<AlertTemplate[]>([]);
	let loading = $state(false);
	let error = $state<string | null>(null);
	let formTarget = $state<AlertTemplate | 'new' | null>(null);
	let saving = $state(false);
	let saveError = $state<string | null>(null);

	async function load(): Promise<void> {
		loading = true;
		error = null;
		try {
			templates = await listAlertTemplates();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			loading = false;
		}
	}

	function openForm(target: AlertTemplate | 'new'): void {
		saveError = null;
		formTarget = target;
	}

	async function save(request: AlertTemplateRequest): Promise<void> {
		const target = formTarget;
		saving = true;
		saveError = null;
		try {
			if (target && target !== 'new') {
				await updateAlertTemplate(target.id, request);
			} else {
				await createAlertTemplate(request);
			}
			formTarget = null;
			await load();
		} catch (err) {
			saveError = err instanceof Error ? err.message : String(err);
		} finally {
			saving = false;
		}
	}

	async function remove(template: AlertTemplate): Promise<void> {
		if (!confirm(m.alertTemplates_deleteConfirm({ name: template.name }))) return;
		try {
			await deleteAlertTemplate(template.id);
			await load();
		} catch (err) {
			// A template still used by rules is refused with a message naming them.
			error = err instanceof Error ? err.message : String(err);
		}
	}

	onMount(() => {
		void load();
	});
</script>

<svelte:head>
	<title>{m.alertTemplates_heading()}</title>
</svelte:head>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.alertTemplates_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.alertTemplates_description()}</p>
	</div>
	<Button size="sm" onclick={() => openForm('new')}>
		<PlusIcon data-icon="inline-start" />
		{m.alertTemplates_new()}
	</Button>
</div>

{#if loading && templates.length === 0}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if error && templates.length === 0}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{error}</p>
	</div>
{:else if templates.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<FileTextIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>{m.alertTemplates_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.alertTemplates_emptyDescription()}</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => openForm('new')}>
				<PlusIcon data-icon="inline-start" />
				{m.alertTemplates_new()}
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	{#if error}
		<p class="text-destructive border-b px-4 py-2 text-xs">{error}</p>
	{/if}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.alertTemplates_colName()}</Table.Head>
					<Table.Head>{m.alertTemplates_colPreview()}</Table.Head>
					<Table.Head class="text-right">{m.alertTemplates_colActions()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each templates as template (template.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{template.name}
							{#if template.isDefault}
								<Badge variant="secondary" class="ml-1">{m.alertTemplates_defaultBadge()}</Badge>
							{/if}
							{#if template.description}
								<p class="text-muted-foreground font-normal">{template.description}</p>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground max-w-md truncate font-mono text-xs">
							{template.titleTemplate || template.bodyTemplate}
						</Table.Cell>
						<Table.Cell class="text-right">
							<Button variant="ghost" size="icon-sm" title={m.alertTemplates_actionEdit()} onclick={() => openForm(template)}>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.alertTemplates_actionDelete()}
								onclick={() => remove(template)}
							>
								<Trash2Icon />
							</Button>
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	</div>
{/if}

<AlertTemplateFormDialog target={formTarget} {saving} {saveError} onSave={save} onClose={() => (formTarget = null)} />
