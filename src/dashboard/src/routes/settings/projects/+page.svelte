<script lang="ts">
	// Admin-only project management (ADR-0123): a project is a named allow-list of service.name
	// patterns plus per-project member roles. Reachable from Settings > Workspace.
	import { onMount } from 'svelte';
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import ProjectFormDialog from '$lib/components/projects/ProjectFormDialog.svelte';
	import ProjectMembersDialog from '$lib/components/projects/ProjectMembersDialog.svelte';
	import { deleteProject, listProjects, type Project } from '$lib/projects-api';
	import { projects as projectStore } from '$lib/projects/store.svelte';
	import FolderIcon from '@lucide/svelte/icons/folder';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import UsersIcon from '@lucide/svelte/icons/users';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import * as m from '$lib/paraglide/messages';

	let rows = $state<Project[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let formTarget = $state<Project | 'new' | null>(null);
	let membersTarget = $state<Project | null>(null);

	async function load(): Promise<void> {
		try {
			rows = await listProjects();
			error = null;
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			loading = false;
		}
		// The switcher and form pickers read the same data through /api/projects/mine.
		void projectStore.load();
	}

	async function onDelete(project: Project): Promise<void> {
		if (!confirm(m.projectsPage_confirmDelete({ name: project.name }))) return;
		try {
			await deleteProject(project.id);
			await load();
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		}
	}

	onMount(() => {
		void load();
	});
</script>

<svelte:head>
	<title>{m.projectsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<div class="flex items-center justify-between border-b px-4 py-3">
		<div>
			<h1 class="text-sm font-semibold">{m.projectsPage_heading()}</h1>
			<p class="text-muted-foreground text-xs">{m.projectsPage_description()}</p>
		</div>
		<Button size="sm" onclick={() => (formTarget = 'new')}>
			<PlusIcon data-icon="inline-start" />
			{m.projectsPage_new()}
		</Button>
	</div>

	{#if loading}
		<div class="flex flex-1 items-center justify-center"><Spinner /></div>
	{:else if error}
		<div class="flex flex-1 items-center justify-center"><p class="text-destructive text-sm">{error}</p></div>
	{:else if rows.length === 0}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Media><FolderIcon class="text-muted-foreground size-8" /></Empty.Media>
				<Empty.Title>{m.projectsPage_emptyTitle()}</Empty.Title>
				<Empty.Description>{m.projectsPage_emptyDescription()}</Empty.Description>
			</Empty.Header>
			<Empty.Content>
				<Button size="sm" onclick={() => (formTarget = 'new')}>
					<PlusIcon data-icon="inline-start" />
					{m.projectsPage_new()}
				</Button>
			</Empty.Content>
		</Empty.Root>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.projectsPage_colName()}</Table.Head>
					<Table.Head>{m.projectsPage_colServices()}</Table.Head>
					<Table.Head class="text-right">{m.projectsPage_colActions()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each rows as project (project.id)}
					<Table.Row>
						<Table.Cell>
							<div class="font-medium">{project.name}</div>
							{#if project.description}<div class="text-muted-foreground text-xs">{project.description}</div>{/if}
						</Table.Cell>
						<Table.Cell>
							<div class="flex flex-wrap gap-1">
								{#each project.servicePatterns as pattern (pattern)}
									<Badge variant="secondary" class="font-mono">{pattern}</Badge>
								{:else}
									<span class="text-muted-foreground text-xs">{m.projectsPage_noServices()}</span>
								{/each}
							</div>
						</Table.Cell>
						<Table.Cell class="text-right">
							<Button variant="ghost" size="icon-sm" title={m.projectsPage_members()} onclick={() => (membersTarget = project)}>
								<UsersIcon />
							</Button>
							<Button variant="ghost" size="icon-sm" title={m.projectsPage_edit()} onclick={() => (formTarget = project)}>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.projectsPage_delete()}
								onclick={() => onDelete(project)}
							>
								<Trash2Icon />
							</Button>
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>

<ProjectFormDialog target={formTarget} onclose={() => (formTarget = null)} onsaved={async () => { formTarget = null; await load(); }} />
<ProjectMembersDialog target={membersTarget} onclose={() => (membersTarget = null)} />
