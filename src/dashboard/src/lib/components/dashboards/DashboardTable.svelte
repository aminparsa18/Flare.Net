<script lang="ts">
	// Mirrors AlertRuleTable.svelte's Table/Empty/header-with-create-button composition -
	// unlike SavedViewTable.svelte (no create flow, see SavedViewsState's remarks), a
	// dashboard is created blank from here, same shape as an alert rule.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import { goto } from '$app/navigation';
	import { dashboardsContext } from '$lib/dashboards/context';
	import { dashboardPath } from '$lib/dashboards/page-paths';
	import type { DashboardSummary } from '$lib/dashboards-api';
	import LayoutDashboardIcon from '@lucide/svelte/icons/layout-dashboard';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import DownloadIcon from '@lucide/svelte/icons/download';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import * as m from '$lib/paraglide/messages';

	const dashboards = dashboardsContext.get();

	function formatDate(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}

	async function handleDelete(dashboard: DashboardSummary): Promise<void> {
		if (!confirm(m.dashboardTable_confirmDelete({ name: dashboard.name }))) return;
		await dashboards.remove(dashboard.id);
	}

	/** Navigates straight to the copy, same "land on what you just made" UX createDashboard's own flow gives from the New dashboard dialog. */
	async function handleDuplicate(dashboard: DashboardSummary): Promise<void> {
		const id = await dashboards.duplicate(dashboard);
		if (id) await goto(dashboardPath({ id }));
	}
</script>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.dashboardTable_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.dashboardTable_description()}</p>
	</div>
	<Button size="sm" onclick={() => dashboards.openCreate()}>
		<PlusIcon data-icon="inline-start" />
		{m.dashboardTable_new()}
	</Button>
</div>

{#if dashboards.loading}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if dashboards.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{dashboards.error}</p>
	</div>
{:else if dashboards.dashboards.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<LayoutDashboardIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>{m.dashboardTable_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.dashboardTable_emptyDescription()}</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => dashboards.openCreate()}>
				<PlusIcon data-icon="inline-start" />
				{m.dashboardTable_new()}
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.dashboardTable_nameColumn()}</Table.Head>
					<Table.Head>{m.dashboardTable_panelsColumn()}</Table.Head>
					<Table.Head>{m.dashboardTable_updatedColumn()}</Table.Head>
					<Table.Head class="text-right">{m.dashboardTable_actionsColumn()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each dashboards.dashboards as dashboard (dashboard.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{dashboard.name}
							{#if dashboard.description}
								<p class="text-muted-foreground font-normal">{dashboard.description}</p>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground">{dashboard.layout.panels.length}</Table.Cell>
						<Table.Cell class="text-muted-foreground">{formatDate(dashboard.updatedAt)}</Table.Cell>
						<Table.Cell class="text-right">
							<Button variant="ghost" size="sm" href={dashboardPath(dashboard)}>{m.dashboardTable_open()}</Button>
							<Button variant="ghost" size="icon-sm" title={m.dashboardTable_rename()} onclick={() => dashboards.openRename(dashboard)}>
								<PencilIcon />
							</Button>
							<Button variant="ghost" size="icon-sm" title={m.dashboardTable_duplicate()} onclick={() => handleDuplicate(dashboard)}>
								<CopyIcon />
							</Button>
							<Button variant="ghost" size="icon-sm" title={m.dashboardTable_export()} onclick={() => dashboards.exportDashboard(dashboard)}>
								<DownloadIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.dashboardTable_delete()}
								onclick={() => handleDelete(dashboard)}
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
