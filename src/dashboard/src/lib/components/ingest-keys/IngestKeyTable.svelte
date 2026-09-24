<script lang="ts">
	// Same header-button + Table/Empty/Badge composition as AccessTokenTable.svelte, plus
	// two usage columns (current UTC minute / UTC day, ADR-0051) showing each count against
	// its cap when one is enforced - the numbers come from the same Redis counters
	// Flare.Ingest enforces against, so "at cap" here means exports are being rejected.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { ingestKeysContext } from '$lib/ingest-keys/context';
	import type { IngestApiKeyDto } from '$lib/ingest-keys-api';
	import { formatBytes, formatCount } from '$lib/ingestion/format';
	import KeyRoundIcon from '@lucide/svelte/icons/key-round';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import GaugeIcon from '@lucide/svelte/icons/gauge';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import * as m from '$lib/paraglide/messages';

	const keys = ingestKeysContext.get();

	function formatDate(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}

	function enforced(key: IngestApiKeyDto): boolean {
		return (
			key.limitsEnabled &&
			(key.maxEventsPerMinute != null || key.maxBytesPerMinute != null || key.maxEventsPerDay != null || key.maxBytesPerDay != null)
		);
	}

	/** Tone for a used/cap pair: at or over the cap is what Flare.Ingest rejects on
	 *  (IngestKeyLimitEvaluator's `used >= cap`), 80%+ is an early warning. */
	function tone(used: number, cap: number | null, active: boolean): string {
		if (!active || cap == null) return '';
		if (used >= cap) return 'text-destructive font-medium';
		if (used >= cap * 0.8) return 'text-warning';
		return '';
	}

	async function handleRevoke(key: IngestApiKeyDto): Promise<void> {
		if (!confirm(m.ingestKeyTable_confirmRevoke({ name: key.name }))) return;
		await keys.revoke(key.id);
	}
</script>

{#snippet usageCell(events: number, eventsCap: number | null, bytes: number, bytesCap: number | null, active: boolean)}
	<div class="flex flex-col text-xs tabular-nums">
		<span class={tone(events, eventsCap, active)}>
			{m.ingestKeyTable_events({ count: formatCount(events) })}{#if active && eventsCap != null}<span class="text-muted-foreground"
					>{` / ${formatCount(eventsCap)}`}</span
				>{/if}
		</span>
		<span class={tone(bytes, bytesCap, active)}>
			{formatBytes(bytes)}{#if active && bytesCap != null}<span class="text-muted-foreground">{` / ${formatBytes(bytesCap)}`}</span>{/if}
		</span>
	</div>
{/snippet}

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.ingestKeyTable_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.ingestKeyTable_description()}</p>
	</div>
	<Button size="sm" onclick={() => keys.openCreate()}>
		<PlusIcon data-icon="inline-start" />
		{m.ingestKeyTable_newKey()}
	</Button>
</div>

{#if keys.loading}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if keys.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{keys.error}</p>
	</div>
{:else if keys.keys.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<KeyRoundIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>{m.ingestKeyTable_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.ingestKeyTable_emptyDescription()}</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => keys.openCreate()}>
				<PlusIcon data-icon="inline-start" />
				{m.ingestKeyTable_newKey()}
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.ingestKeyTable_nameColumn()}</Table.Head>
					<Table.Head>{m.ingestKeyTable_statusColumn()}</Table.Head>
					<Table.Head>{m.ingestKeyTable_createdColumn()}</Table.Head>
					<Table.Head>{m.ingestKeyTable_limitsColumn()}</Table.Head>
					<Table.Head>{m.ingestKeyTable_thisMinuteColumn()}</Table.Head>
					<Table.Head>{m.ingestKeyTable_todayColumn()}</Table.Head>
					<Table.Head class="text-right">{m.ingestKeyTable_actionsColumn()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each keys.keys as key (key.id)}
					{@const active = enforced(key)}
					<Table.Row>
						<Table.Cell class="font-medium">{key.name}</Table.Cell>
						<Table.Cell>
							{#if key.isActive}
								<Badge variant="outline">{m.ingestKeyTable_statusActive()}</Badge>
							{:else}
								<Badge variant="destructive">{m.ingestKeyTable_statusRevoked()}</Badge>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground">{formatDate(key.createdAt)}</Table.Cell>
						<Table.Cell>
							{#if active}
								<Badge variant="secondary">{m.ingestKeyTable_limitsEnforced()}</Badge>
							{:else}
								<span class="text-muted-foreground text-xs">{m.ingestKeyTable_limitsNone()}</span>
							{/if}
						</Table.Cell>
						<Table.Cell>
							{#if key.isActive}
								{@render usageCell(key.eventsThisMinute, key.maxEventsPerMinute, key.bytesThisMinute, key.maxBytesPerMinute, active)}
							{:else}
								<span class="text-muted-foreground">—</span>
							{/if}
						</Table.Cell>
						<Table.Cell>
							{#if key.isActive}
								{@render usageCell(key.eventsToday, key.maxEventsPerDay, key.bytesToday, key.maxBytesPerDay, active)}
							{:else}
								<span class="text-muted-foreground">—</span>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-right">
							<Button
								variant="ghost"
								size="icon-sm"
								title={m.ingestKeyTable_editLimits()}
								disabled={!key.isActive}
								onclick={() => keys.openLimits(key)}
							>
								<GaugeIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.ingestKeyTable_revoke()}
								disabled={!key.isActive}
								onclick={() => handleRevoke(key)}
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
