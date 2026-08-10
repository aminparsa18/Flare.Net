<script lang="ts">
	// Mirrors AlertRuleTable.svelte's Table/Empty/Badge composition.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { savedViewsContext } from '$lib/saved-views/context';
	import { savedViewPath } from '$lib/saved-views/page-paths';
	import type { SavedView } from '$lib/saved-views-api';
	import LayoutGridIcon from '@lucide/svelte/icons/layout-grid';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import LinkIcon from '@lucide/svelte/icons/link';
	import CheckIcon from '@lucide/svelte/icons/check';

	const views = savedViewsContext.get();

	function formatDate(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}

	async function handleDelete(view: SavedView): Promise<void> {
		if (!confirm(`Delete saved view "${view.name}"? This cannot be undone.`)) return;
		await views.remove(view.id);
	}

	// Ephemeral, per-row "copied" confirmation - not part of SavedViewsState since nothing
	// else in the app needs to react to it, same "local UI-only concern" reasoning
	// AlertRuleTable.svelte's own testResults field documents.
	let copiedId = $state<string | null>(null);

	async function handleCopyLink(view: SavedView): Promise<void> {
		const url = `${location.origin}${savedViewPath(view)}`;
		await navigator.clipboard.writeText(url);
		copiedId = view.id;
		setTimeout(() => {
			if (copiedId === view.id) copiedId = null;
		}, 1500);
	}
</script>

<div class="border-b px-4 py-3">
	<h1 class="text-sm font-semibold">Views</h1>
	<p class="text-muted-foreground text-xs">
		Named, reloadable filters saved from Logs, Traces, or Metrics. Save one from a page's own toolbar.
	</p>
</div>

{#if views.loading}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if views.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{views.error}</p>
	</div>
{:else if views.views.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<LayoutGridIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>No saved views yet</Empty.Title>
			<Empty.Description>Save one from the "Views" control in the Logs, Traces, or Metrics toolbar.</Empty.Description>
		</Empty.Header>
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>Name</Table.Head>
					<Table.Head>Page</Table.Head>
					<Table.Head>Updated</Table.Head>
					<Table.Head class="text-right">Actions</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each views.views as view (view.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{view.name}
							{#if view.description}
								<p class="text-muted-foreground font-normal">{view.description}</p>
							{/if}
						</Table.Cell>
						<Table.Cell><Badge variant="outline">{view.pageType}</Badge></Table.Cell>
						<Table.Cell class="text-muted-foreground">{formatDate(view.updatedAt)}</Table.Cell>
						<Table.Cell class="text-right">
							<Button variant="ghost" size="sm" href={savedViewPath(view)}>Open</Button>
							<Button variant="ghost" size="icon-sm" title="Copy shareable link" onclick={() => handleCopyLink(view)}>
								{#if copiedId === view.id}
									<CheckIcon />
								{:else}
									<LinkIcon />
								{/if}
							</Button>
							<Button variant="ghost" size="icon-sm" title="Rename" onclick={() => views.openRename(view)}>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title="Delete"
								onclick={() => handleDelete(view)}
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
