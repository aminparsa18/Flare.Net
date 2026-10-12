<script lang="ts">
	// Forwarding targets (ADR-0155/0157): the saved targets plus read-only rows for the ones defined in
	// configuration, each with live queue/delivery status polled from /api/forwarding/status.
	import { onDestroy, onMount } from 'svelte';
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Switch } from '$lib/components/ui/switch';
	import { listIngestApiKeys, type IngestApiKeyDto } from '$lib/ingest-keys-api';
	import {
		deleteForwardingTarget,
		getForwardingStatus,
		listForwardingTargets,
		saveForwardingTarget,
		type ForwardingTarget,
		type ForwardingTargetStatus
	} from '$lib/telemetry-export-api';
	import { singleLineError, statusKeyForTarget } from '$lib/telemetry-export/format';
	import { formatAge, formatCount, secondsSince } from '$lib/ingestion/format';
	import ForwardingTargetFormDialog from './ForwardingTargetFormDialog.svelte';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';

	const POLL_MS = 5000;

	let targets = $state.raw<ForwardingTarget[] | null>(null);
	let statuses = $state.raw<ForwardingTargetStatus[]>([]);
	let keys = $state.raw<IngestApiKeyDto[]>([]);
	let error = $state<string | null>(null);
	/** `'new'` opens the create dialog, a target opens it for editing, null = closed. */
	let formTarget = $state<ForwardingTarget | 'new' | null>(null);
	let timer: ReturnType<typeof setTimeout> | undefined;
	let controller: AbortController | undefined;

	const statusByKey = $derived(new Map(statuses.map((s) => [s.key, s])));
	const configRows = $derived(statuses.filter((s) => s.source === 'config'));
	const keyNames = $derived(new Map(keys.map((k) => [k.id, k.name])));

	async function loadTargets() {
		try {
			targets = await listForwardingTargets();
			error = null;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
	}

	async function pollStatus() {
		controller = new AbortController();
		try {
			statuses = await getForwardingStatus(controller.signal);
		} catch (err) {
			if (!(err instanceof DOMException && err.name === 'AbortError')) error = err instanceof Error ? err.message : String(err);
		}
		timer = setTimeout(pollStatus, POLL_MS);
	}

	async function setEnabled(target: ForwardingTarget, enabled: boolean) {
		try {
			// Headers go back masked, which the server reads as "keep the stored value".
			await saveForwardingTarget(target.id, { ...target, enabled });
			await loadTargets();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
	}

	async function remove(target: ForwardingTarget) {
		if (!confirm(m.telemetryExport_fwdDeleteConfirm({ name: target.name }))) return;
		try {
			await deleteForwardingTarget(target.id);
			await Promise.all([loadTargets(), pollNow()]);
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
	}

	async function pollNow() {
		try {
			statuses = await getForwardingStatus();
		} catch {
			// The next poll reports it.
		}
	}

	function saved() {
		formTarget = null;
		void loadTargets();
	}

	onMount(() => {
		void loadTargets();
		void pollStatus();
		listIngestApiKeys()
			.then((k) => (keys = k))
			.catch(() => {
				// Key names are cosmetic here; the dialog degrades to "no keys".
			});
	});

	onDestroy(() => {
		clearTimeout(timer);
		controller?.abort();
	});

	function summarize(items: string[]): string {
		return items.length === 0 ? m.telemetryExport_all() : items.join(', ');
	}
</script>

{#snippet statusCells(status: ForwardingTargetStatus | undefined)}
	<Table.Cell class="text-right tabular-nums">{status ? formatCount(status.pending) : '—'}</Table.Cell>
	<Table.Cell class="text-right tabular-nums">{status ? formatCount(status.sent) : '—'}</Table.Cell>
	<Table.Cell class="text-right tabular-nums">{status ? formatCount(status.failed) : '—'}</Table.Cell>
	<Table.Cell class="max-w-xs truncate font-mono text-xs" title={singleLineError(status?.lastError ?? null) ?? undefined}>
		{#if status?.lastError}
			<!-- An error older than the last success is history, not a live alarm. -->
			{@const stale = status.lastSuccessAt && status.lastErrorAt && new Date(status.lastSuccessAt) > new Date(status.lastErrorAt)}
			<span class={stale ? 'text-muted-foreground' : 'text-destructive'}>{singleLineError(status.lastError)}</span>
			{#if status.lastErrorAt}
				<span class="text-muted-foreground"> · {formatAge(secondsSince(status.lastErrorAt))}</span>
			{/if}
		{:else if status?.lastSuccessAt}
			<span class="text-muted-foreground">{m.telemetryExport_fwdLastOk({ age: formatAge(secondsSince(status.lastSuccessAt)) })}</span>
		{:else}
			<span class="text-muted-foreground">—</span>
		{/if}
	</Table.Cell>
{/snippet}

<section class="flex flex-col gap-2">
	<div class="flex items-start justify-between gap-4">
		<div>
			<h2 class="text-base font-semibold">{m.telemetryExport_fwdHeading()}</h2>
			<p class="text-muted-foreground text-sm">{m.telemetryExport_fwdIntro()}</p>
		</div>
		<Button size="sm" onclick={() => (formTarget = 'new')}>
			<PlusIcon data-icon="inline-start" />
			{m.telemetryExport_fwdNew()}
		</Button>
	</div>

	{#if error}
		<p class="text-destructive text-sm">{error}</p>
	{/if}

	{#if targets === null}
		{#if !error}
			<div class="flex h-24 items-center justify-center"><Spinner /></div>
		{/if}
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.telemetryExport_fwdColName()}</Table.Head>
					<Table.Head>{m.telemetryExport_fwdColEndpoint()}</Table.Head>
					<Table.Head>{m.telemetryExport_signalsLabel()}</Table.Head>
					<Table.Head>{m.telemetryExport_fwdColServices()}</Table.Head>
					<Table.Head class="text-right">{m.telemetryExport_fwdColPending()}</Table.Head>
					<Table.Head class="text-right">{m.telemetryExport_fwdColSent()}</Table.Head>
					<Table.Head class="text-right">{m.telemetryExport_fwdColFailed()}</Table.Head>
					<Table.Head>{m.telemetryExport_fwdColLast()}</Table.Head>
					<Table.Head>{m.telemetryExport_enabledLabel()}</Table.Head>
					<Table.Head class="text-right">{m.synthetic_colActions()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each targets as target (target.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{target.name}
							{#if target.ingestKeyIds.length > 0}
								<p class="text-muted-foreground text-xs font-normal">
									{m.telemetryExport_fwdKeysLabel()}: {target.ingestKeyIds.map((id) => keyNames.get(id) ?? id.slice(0, 8)).join(', ')}
								</p>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground max-w-60 truncate font-mono text-xs" title={target.endpoint}>{target.endpoint}</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs">{summarize(target.signals)}</Table.Cell>
						<Table.Cell class="text-muted-foreground max-w-40 truncate text-xs" title={target.services.join(', ')}>{summarize(target.services)}</Table.Cell>
						{@render statusCells(statusByKey.get(statusKeyForTarget(target.id)))}
						<Table.Cell>
							<Switch size="sm" checked={target.enabled} onCheckedChange={(v) => setEnabled(target, v)} />
						</Table.Cell>
						<Table.Cell class="text-right">
							<Button variant="ghost" size="icon-sm" title={m.synthetic_actionEdit()} onclick={() => (formTarget = target)}>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.synthetic_actionDelete()}
								onclick={() => remove(target)}
							>
								<Trash2Icon />
							</Button>
						</Table.Cell>
					</Table.Row>
				{/each}
				{#each configRows as row (row.key)}
					<Table.Row class="bg-muted/30">
						<Table.Cell class="font-medium">
							{row.name}
							<Badge variant="outline" class="ml-1 font-normal">{m.telemetryExport_fwdFromConfig()}</Badge>
						</Table.Cell>
						<Table.Cell class="text-muted-foreground">—</Table.Cell>
						<Table.Cell class="text-muted-foreground">—</Table.Cell>
						<Table.Cell class="text-muted-foreground">—</Table.Cell>
						{@render statusCells(row)}
						<Table.Cell class="text-muted-foreground">—</Table.Cell>
						<Table.Cell></Table.Cell>
					</Table.Row>
				{/each}
				{#if targets.length === 0 && configRows.length === 0}
					<Table.Row>
						<Table.Cell colspan={10} class="text-muted-foreground py-6 text-center text-sm">{m.telemetryExport_fwdEmpty()}</Table.Cell>
					</Table.Row>
				{/if}
			</Table.Body>
		</Table.Root>
	{/if}
</section>

{#if formTarget !== null}
	<ForwardingTargetFormDialog target={formTarget === 'new' ? null : formTarget} {keys} onsaved={saved} onclose={() => (formTarget = null)} />
{/if}
