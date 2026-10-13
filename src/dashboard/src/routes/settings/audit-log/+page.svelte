<script lang="ts">
	// Admin-only audit log (ADR-0079): who changed an alert rule, channel, dashboard, user
	// role, auth setting, ... and when. Newest first, keyset-paged with "Load more".
	// Reachable from NavUserMenu's dropdown, like /ingest-keys.
	import { onMount } from 'svelte';
	import * as Table from '#lib/components/ui/table/index.ts';
	import * as Empty from '#lib/components/ui/empty/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Badge } from '#lib/components/ui/badge/index.ts';
	import { Spinner } from '#lib/components/ui/spinner/index.ts';
	import ScrollTextIcon from '@lucide/svelte/icons/scroll-text';
	import {
		AUDIT_RESOURCE_TYPES,
		downloadAuditExport,
		listAuditEvents,
		verifyAuditChain,
		type AuditChainVerification,
		type AuditEvent,
		type AuditExportFormat
	} from '#lib/audit-api.ts';
	import { formatDateTime } from '#lib/time/format.ts';
	import * as m from '#lib/paraglide/messages.js';

	const PAGE_SIZE = 100;

	let events = $state<AuditEvent[]>([]);
	let nextBefore = $state<number | null>(null);
	let loading = $state(true);
	let loadingMore = $state(false);
	let error = $state<string | null>(null);
	let resourceType = $state('');
	let verifying = $state(false);
	let verification = $state<AuditChainVerification | null>(null);
	let actionError = $state<string | null>(null);

	async function exportAs(format: AuditExportFormat): Promise<void> {
		actionError = null;
		try {
			await downloadAuditExport(format, { resourceType: resourceType || undefined });
		} catch (e) {
			actionError = e instanceof Error ? e.message : String(e);
		}
	}

	async function verify(): Promise<void> {
		verifying = true;
		actionError = null;
		try {
			verification = await verifyAuditChain();
		} catch (e) {
			actionError = e instanceof Error ? e.message : String(e);
		} finally {
			verifying = false;
		}
	}

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
		<div class="flex items-center gap-2">
		<Button variant="outline" size="sm" onclick={() => void exportAs('csv')}>{m.auditLogPage_exportCsv()}</Button>
		<Button variant="outline" size="sm" onclick={() => void exportAs('json')}>{m.auditLogPage_exportJson()}</Button>
		<Button variant="outline" size="sm" disabled={verifying} onclick={() => void verify()}>{m.auditLogPage_verify()}</Button>
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
	</div>

	{#if verification}
		<p
			class="border-b px-4 py-2 text-xs {verification.valid ? 'text-muted-foreground' : 'text-destructive'}"
			role="status"
		>
			{#if verification.valid}
				{m.auditLogPage_verifyOk({ chained: verification.chainedEvents, legacy: verification.legacyEvents })}
			{:else}
				{m.auditLogPage_verifyBroken({ id: verification.firstBrokenId ?? 0, reason: verification.reason ?? '' })}
			{/if}
		</p>
	{/if}
	{#if actionError}
		<p class="text-destructive border-b px-4 py-2 text-xs" role="alert">{actionError}</p>
	{/if}

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
						<Table.Head>{m.auditLogPage_changesColumn()}</Table.Head>
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
							<Table.Cell class="align-top">
								{#if event.changes.length > 0}
									<details class="text-xs">
										<summary class="text-muted-foreground cursor-pointer select-none">
											{m.auditLogPage_changesCount({ count: event.changes.length })}
										</summary>
										<ul class="mt-1 space-y-1">
											{#each event.changes as change (change.field)}
												<li>
													<span class="font-mono font-medium">{change.field}</span>
													<span class="text-muted-foreground font-mono break-all">
														{change.before ?? m.auditLogPage_changeEmptyValue()} → {change.after ?? m.auditLogPage_changeEmptyValue()}
													</span>
												</li>
											{/each}
										</ul>
									</details>
								{:else}
									<span class="text-muted-foreground">—</span>
								{/if}
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
