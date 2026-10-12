<script lang="ts">
	// Data-subject erasure and export (ADR-0198): find one person's telemetry by attribute, preview what
	// matches, export it, or delete it. ClickHouse applies a delete asynchronously, so while one is
	// running the page polls for per-table progress.
	import { onDestroy, onMount } from 'svelte';
	import * as Card from '$lib/components/ui/card';
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Spinner } from '$lib/components/ui/spinner';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import XIcon from '@lucide/svelte/icons/x';
	import {
		COMMON_IDENTIFIER_KEYS,
		exportSubject,
		getErasures,
		previewErasure,
		startErasure,
		type ErasureIdentifier,
		type ErasureOperation,
		type ErasurePreview,
		type ErasureStatus
	} from '$lib/erasure-api';
	import { downloadBlob } from '$lib/logs/export';
	import * as m from '$lib/paraglide/messages';

	let rows = $state<ErasureIdentifier[]>([{ key: 'user.id', value: '' }]);
	let preview = $state.raw<ErasurePreview | null>(null);
	// The identifiers the preview was computed for; changing a row afterwards invalidates it.
	let previewed = $state('');
	let confirmText = $state('');
	let busy = $state<'preview' | 'export' | 'erase' | null>(null);
	let error = $state<string | null>(null);
	let operations = $state.raw<ErasureOperation[]>([]);
	let timer: ReturnType<typeof setTimeout> | undefined;

	const identifiers = $derived(rows.filter((r) => r.key.trim() && r.value).map((r) => ({ key: r.key.trim(), value: r.value })));
	const signature = $derived(JSON.stringify(identifiers));
	const ready = $derived(identifiers.length > 0 && identifiers.length === rows.length);
	const previewCurrent = $derived(preview !== null && previewed === signature);
	const canErase = $derived(previewCurrent && preview!.totalRows > 0 && confirmText === identifiers[0]?.value && busy === null);

	const active = (s: ErasureStatus) => s === 'pending' || s === 'running';

	async function load() {
		try {
			operations = await getErasures();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
		clearTimeout(timer);
		if (operations.some((o) => active(o.status))) timer = setTimeout(load, 3000);
	}

	async function run<T>(kind: 'preview' | 'export' | 'erase', work: () => Promise<T>): Promise<T | undefined> {
		busy = kind;
		error = null;
		try {
			return await work();
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			busy = null;
		}
	}

	async function doPreview() {
		const result = await run('preview', () => previewErasure(identifiers));
		if (result) {
			preview = result;
			previewed = signature;
			confirmText = '';
		}
	}

	async function doExport() {
		const blob = await run('export', () => exportSubject(identifiers));
		if (blob) downloadBlob(blob, 'flare-subject-export.ndjson');
	}

	async function doErase() {
		const id = await run('erase', () => startErasure(identifiers, confirmText));
		if (id) {
			preview = null;
			confirmText = '';
			await load();
		}
	}

	const statusLabel = (s: ErasureStatus) =>
		({ pending: m.erasure_statusPending(), running: m.erasure_statusRunning(), success: m.erasure_statusSuccess(), failed: m.erasure_statusFailed() })[s];
	const statusVariant = (s: ErasureStatus) => (s === 'failed' ? 'destructive' : s === 'success' ? 'default' : 'secondary');

	onMount(() => void load());
	onDestroy(() => clearTimeout(timer));
</script>

<svelte:head>
	<title>{m.erasure_title()}</title>
</svelte:head>

<div class="flex flex-col gap-4 p-4">
	<div>
		<h1 class="text-lg font-semibold">{m.erasure_heading()}</h1>
		<p class="text-muted-foreground text-sm">{m.erasure_intro()}</p>
	</div>

	{#if error}
		<p class="text-destructive text-sm" role="alert">{error}</p>
	{/if}

	<Card.Root>
		<Card.Header>
			<Card.Title>{m.erasure_identifiersHeading()}</Card.Title>
			<Card.Description>{m.erasure_identifiersHint()}</Card.Description>
		</Card.Header>
		<Card.Content class="flex flex-col gap-3">
			<datalist id="erasure-keys">
				{#each COMMON_IDENTIFIER_KEYS as key (key)}<option value={key}></option>{/each}
			</datalist>
			{#each rows as row, i (i)}
				<div class="flex items-center gap-2">
					<Input class="w-52" list="erasure-keys" placeholder={m.erasure_keyPlaceholder()} bind:value={row.key} aria-label={m.erasure_keyPlaceholder()} />
					<span class="text-muted-foreground">=</span>
					<Input class="flex-1" placeholder={m.erasure_valuePlaceholder()} bind:value={row.value} aria-label={m.erasure_valuePlaceholder()} />
					<Button variant="ghost" size="icon" disabled={rows.length === 1} onclick={() => rows.splice(i, 1)} aria-label={m.erasure_removeIdentifier()}>
						<XIcon class="size-4" />
					</Button>
				</div>
			{/each}
			<div class="flex flex-wrap gap-2">
				<Button variant="outline" size="sm" disabled={rows.length >= 10} onclick={() => rows.push({ key: '', value: '' })}>
					<PlusIcon class="size-4" />
					{m.erasure_addIdentifier()}
				</Button>
				<div class="flex-1"></div>
				<Button variant="outline" disabled={!ready || busy !== null} onclick={doExport}>
					{#if busy === 'export'}<Spinner />{/if}
					{m.erasure_export()}
				</Button>
				<Button disabled={!ready || busy !== null} onclick={doPreview}>
					{#if busy === 'preview'}<Spinner />{/if}
					{m.erasure_preview()}
				</Button>
			</div>
		</Card.Content>
	</Card.Root>

	{#if preview}
		<Card.Root>
			<Card.Header>
				<Card.Title>{m.erasure_previewHeading()}</Card.Title>
				{#if !previewCurrent}
					<Card.Description class="text-destructive">{m.erasure_previewStale()}</Card.Description>
				{/if}
			</Card.Header>
			<Card.Content class="flex flex-col gap-4">
				{#if preview.totalRows === 0}
					<p class="text-muted-foreground text-sm">{m.erasure_nothingFound()}</p>
				{:else}
					<Table.Root>
						<Table.Header>
							<Table.Row>
								<Table.Head>{m.erasure_colTable()}</Table.Head>
								<Table.Head class="text-right">{m.erasure_colRows()}</Table.Head>
							</Table.Row>
						</Table.Header>
						<Table.Body>
							{#each preview.tables.filter((t) => t.rows > 0) as t (t.table)}
								<Table.Row>
									<Table.Cell class="font-mono">{t.table}</Table.Cell>
									<Table.Cell class="text-right tabular-nums">{t.rows.toLocaleString()}</Table.Cell>
								</Table.Row>
							{/each}
						</Table.Body>
					</Table.Root>
					<p class="text-muted-foreground text-sm">{m.erasure_total({ rows: preview.totalRows.toLocaleString(), sessions: preview.sessionCount.toLocaleString() })}</p>
					{#if preview.sessionsTruncated}
						<p class="text-sm text-amber-600 dark:text-amber-400">{m.erasure_sessionsTruncated()}</p>
					{/if}

					<div class="border-destructive/40 flex flex-col gap-2 rounded-md border p-3">
						<p class="text-sm font-medium">{m.erasure_eraseHeading()}</p>
						<p class="text-muted-foreground text-sm">{m.erasure_eraseWarning()}</p>
						<label class="text-sm" for="erasure-confirm">{m.erasure_confirmLabel({ value: identifiers[0]?.value ?? '' })}</label>
						<div class="flex gap-2">
							<Input id="erasure-confirm" class="flex-1" autocomplete="off" bind:value={confirmText} />
							<Button variant="destructive" disabled={!canErase} onclick={doErase}>
								{#if busy === 'erase'}<Spinner />{/if}
								{m.erasure_erase()}
							</Button>
						</div>
					</div>
				{/if}
			</Card.Content>
		</Card.Root>
	{/if}

	<Card.Root>
		<Card.Header><Card.Title>{m.erasure_historyHeading()}</Card.Title></Card.Header>
		<Card.Content class="flex flex-col gap-4">
			{#each operations as op (op.transactionId)}
				<div class="flex flex-col gap-2 border-b pb-4 last:border-b-0 last:pb-0">
					<div class="flex flex-wrap items-center gap-2 text-sm">
						<Badge variant={statusVariant(op.status)}>{statusLabel(op.status)}</Badge>
						<span class="font-mono">{op.identifierKeys.join(', ')}</span>
						<span class="text-muted-foreground">· {op.subjectHash} · {op.requestedBy ? m.erasure_requestedBy({ user: op.requestedBy, time: new Date(op.requestedAt).toLocaleString() }) : new Date(op.requestedAt).toLocaleString()}</span>
					</div>
					{#if op.error}<p class="text-destructive text-sm">{op.error}</p>{/if}
					{#if op.tables.length}
						<Table.Root>
							<Table.Body>
								{#each op.tables as t (t.table)}
									<Table.Row>
										<Table.Cell class="font-mono">{t.table}</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{t.rows.toLocaleString()}</Table.Cell>
										<Table.Cell>
											<Badge variant={statusVariant(t.status)}>{statusLabel(t.status)}</Badge>
											{#if t.status === 'running' && t.partsToDo > 0}
												<span class="text-muted-foreground ml-2 text-xs">{m.erasure_partsLeft({ count: t.partsToDo })}</span>
											{/if}
											{#if t.error}<span class="text-destructive ml-2 text-xs">{t.error}</span>{/if}
										</Table.Cell>
									</Table.Row>
								{/each}
							</Table.Body>
						</Table.Root>
					{/if}
				</div>
			{:else}
				<p class="text-muted-foreground text-sm">{m.erasure_historyEmpty()}</p>
			{/each}
		</Card.Content>
	</Card.Root>
</div>
