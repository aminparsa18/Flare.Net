<script lang="ts">
	// Admin-only audit log (ADR-0079): who changed an alert rule, channel, dashboard, user
	// role, auth setting, ... and when. Newest first, keyset-paged with "Load more".
	// Reachable from NavUserMenu's dropdown, like /ingest-keys.
	import { onMount } from 'svelte';
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import ScrollTextIcon from '@lucide/svelte/icons/scroll-text';
	import { AUDIT_RESOURCE_TYPES, listAuditEvents, type AuditEvent } from '$lib/audit-api';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const PAGE_SIZE = 100;

	let events = $state<AuditEvent[]>([]);
	let nextBefore = $state<number | null>(null);
	let loading = $state(true);
	let loadingMore = $state(false);
	let error = $state<string | null>(null);
	let resourceType = $state('');

	async function load(more = false): Promise<void> {
		if (more) loadingMore = true;
		else loading = true;
		error = null;
		try {
			const page = await listAuditEvents({
				resourceType: resourceType || undefined,
				before: more && nextBefore != null ? nextBefore : undefined,
				limit: PAGE_SIZE
			});
			events = more ? [...events, ...page.events] : page.events;
			nextBefore = page.nextBefore;
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			loading = false;
			loadingMore = false;
		}
	}

	onMount(() => void load());
</script>

<svelte:head>
	<title>{m.auditLogPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<div class="flex items-center justify-between gap-4 border-b px-4 py-3">
		<div>
			<h1 class="text-sm font-semibold">{m.auditLogPage_heading()}</h1>
			<p class="text-muted-foreground text-xs">{m.auditLogPage_description()}</p>
		</div>
		<select
			class="border-input bg-background h-8 rounded-md border px-2 text-sm"
			aria-label={m.auditLogPage_resourceFilter()}
			bind:value={resourceType}
			onchange={() => void load()}
		>
			<option value="">{m.auditLogPage_allResources()}</option>
			{#each AUDIT_RESOURCE_TYPES as type (type)}
				<option value={type}>{type}</option>
			{/each}
		</select>
	</div>

	{#if loading}
		<div class="flex flex-1 items-center justify-center"><Spinner /></div>
	{:else if error}
		<div class="flex flex-1 items-center justify-center"><p class="text-destructive text-sm">{error}</p></div>
	{:else if events.length === 0}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Media><ScrollTextIcon class="text-muted-foreground size-8" /></Empty.Media>
				<Empty.Title>{m.auditLogPage_emptyTitle()}</Empty.Title>
				<Empty.Description>{m.auditLogPage_emptyDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<div class="min-h-0 flex-1 overflow-y-auto">
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.auditLogPage_timeColumn()}</Table.Head>
						<Table.Head>{m.auditLogPage_actorColumn()}</Table.Head>
						<Table.Head>{m.auditLogPage_actionColumn()}</Table.Head>
						<Table.Head>{m.auditLogPage_resourceColumn()}</Table.Head>
						<Table.Head>{m.auditLogPage_sourceColumn()}</Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each events as event (event.id)}
						<Table.Row>
							<Table.Cell class="text-muted-foreground whitespace-nowrap">{formatDateTime(event.timestamp)}</Table.Cell>
							<Table.Cell>
								<span class="font-medium">{event.actorName}</span>
								{#if event.actorKind === 'pat'}<Badge variant="secondary" class="ml-1">{m.auditLogPage_viaToken()}</Badge>{/if}
							</Table.Cell>
							<Table.Cell><Badge variant="outline">{event.action}</Badge></Table.Cell>
							<Table.Cell>
								<span>{event.resourceType}</span>
								{#if event.resourceId}<span class="text-muted-foreground ml-1 font-mono text-xs">{event.resourceId}</span>{/if}
							</Table.Cell>
							<Table.Cell class="text-muted-foreground font-mono text-xs">{event.sourceIp ?? '—'}</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
			{#if nextBefore != null}
				<div class="flex justify-center p-3">
					<Button variant="outline" size="sm" disabled={loadingMore} onclick={() => void load(true)}>
						{m.auditLogPage_loadMore()}
					</Button>
				</div>
			{/if}
		</div>
	{/if}
</div>
