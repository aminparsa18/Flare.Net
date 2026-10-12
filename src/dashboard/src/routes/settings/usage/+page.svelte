<script lang="ts">
	// Usage (roadmap "Usage and cost view"): what is driving storage - per-service volume,
	// per-ingest-key volume today, and the largest attribute keys - so users can pick sampling
	// or retention rules with evidence. Read-only; no polling.
	import { onMount } from 'svelte';
	import * as Card from '$lib/components/ui/card';
	import * as Table from '$lib/components/ui/table';
	import { Spinner } from '$lib/components/ui/spinner';
	import { getUsage, type UsageResponse, type UsageSignal } from '$lib/usage-api';
	import { formatBytes, formatCount } from '$lib/ingestion/format';
	import * as m from '$lib/paraglide/messages';

	const WINDOWS = [1, 7, 30] as const;
	const SIGNALS: UsageSignal[] = ['Logs', 'Traces', 'Metrics'];

	let days = $state<number>(7);
	let data = $state.raw<UsageResponse | null>(null);
	let error = $state<string | null>(null);
	let loading = $state(false);

	async function load() {
		loading = true;
		try {
			data = await getUsage(days);
			error = null;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			loading = false;
		}
	}

	function pick(value: number) {
		days = value;
		void load();
	}

	function signalLabel(signal: UsageSignal): string {
		return signal === 'Logs' ? m.usage_signalLogs() : signal === 'Traces' ? m.usage_signalTraces() : m.usage_signalMetrics();
	}

	const attributeShare = $derived.by(() => {
		const totals = new Map<string, number>();
		for (const a of data?.attributes ?? []) {
			const key = `${a.signal}/${a.scope}`;
			totals.set(key, (totals.get(key) ?? 0) + a.sampledBytes);
		}
		return (a: { signal: string; scope: string; sampledBytes: number }) => {
			const total = totals.get(`${a.signal}/${a.scope}`) ?? 0;
			return total > 0 ? Math.round((a.sampledBytes / total) * 100) : 0;
		};
	});

	onMount(() => void load());
</script>

<svelte:head>
	<title>{m.usage_title()}</title>
</svelte:head>

<div class="flex flex-col gap-4 p-4">
	<div class="flex flex-wrap items-start justify-between gap-2">
		<div>
			<h1 class="text-lg font-semibold">{m.usage_heading()}</h1>
			<p class="text-muted-foreground text-sm">{m.usage_intro()}</p>
		</div>
		<div class="flex gap-1" role="group" aria-label={m.usage_window()}>
			{#each WINDOWS as w (w)}
				<button
					type="button"
					class="rounded-md border px-2.5 py-1 text-xs {days === w ? 'bg-accent font-medium' : 'text-muted-foreground'}"
					aria-pressed={days === w}
					onclick={() => pick(w)}>{m.usage_windowDays({ days: w })}</button
				>
			{/each}
		</div>
	</div>

	{#if error}
		<p class="text-destructive text-sm">{error}</p>
	{/if}

	{#if !data}
		{#if !error}
			<div class="flex h-32 items-center justify-center"><Spinner /></div>
		{/if}
	{:else}
		<div class="grid gap-3 sm:grid-cols-3">
			{#each data.signals as s (s.signal)}
				<Card.Root>
					<Card.Header><Card.Title class="text-sm">{signalLabel(s.signal)}</Card.Title></Card.Header>
					<Card.Content>
						<div class="text-2xl font-semibold">{formatCount(s.events)}</div>
						<div class="text-muted-foreground text-xs">
							{m.usage_signalDetail({ days: data.days, bytes: formatBytes(s.compressedBytes) })}
						</div>
					</Card.Content>
				</Card.Root>
			{/each}
		</div>

		<Card.Root>
			<Card.Header><Card.Title>{m.usage_servicesHeading()}</Card.Title></Card.Header>
			<Card.Content>
				<p class="text-muted-foreground mb-2 text-xs">{m.usage_servicesNote()}</p>
				{#each SIGNALS as signal (signal)}
					{@const rows = data.services.filter((r) => r.signal === signal)}
					{#if rows.length > 0}
						<h3 class="mt-3 mb-1 text-sm font-medium">{signalLabel(signal)}</h3>
						<Table.Root>
							<Table.Header>
								<Table.Row>
									<Table.Head>{m.usage_colService()}</Table.Head>
									<Table.Head class="text-right">{m.usage_colEvents()}</Table.Head>
									<Table.Head class="text-right">{m.usage_colPerDay()}</Table.Head>
									<Table.Head class="text-right">{m.usage_colEstBytes()}</Table.Head>
								</Table.Row>
							</Table.Header>
							<Table.Body>
								{#each rows as r (r.serviceName)}
									<Table.Row>
										<Table.Cell class="font-mono text-xs">{r.serviceName || '—'}</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{formatCount(r.events)}</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{formatCount(r.events / data.days)}</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{formatBytes(r.estimatedBytes)}</Table.Cell>
									</Table.Row>
								{/each}
							</Table.Body>
						</Table.Root>
					{/if}
				{/each}
				{#if data.services.length === 0}
					<p class="text-muted-foreground text-sm">{m.usage_empty()}</p>
				{/if}
			</Card.Content>
		</Card.Root>

		<Card.Root>
			<Card.Header><Card.Title>{m.usage_keysHeading()}</Card.Title></Card.Header>
			<Card.Content>
				<p class="text-muted-foreground mb-2 text-xs">{m.usage_keysNote()}</p>
				{#if data.ingestKeys.length === 0}
					<p class="text-muted-foreground text-sm">{m.usage_keysEmpty()}</p>
				{:else}
					<Table.Root>
						<Table.Header>
							<Table.Row>
								<Table.Head>{m.usage_colKey()}</Table.Head>
								<Table.Head class="text-right">{m.usage_colEventsToday()}</Table.Head>
								<Table.Head class="text-right">{m.usage_colBytesToday()}</Table.Head>
							</Table.Row>
						</Table.Header>
						<Table.Body>
							{#each data.ingestKeys as k (k.id)}
								<Table.Row>
									<Table.Cell>{k.name}</Table.Cell>
									<Table.Cell class="text-right tabular-nums">{formatCount(k.eventsToday)}</Table.Cell>
									<Table.Cell class="text-right tabular-nums">{formatBytes(k.bytesToday)}</Table.Cell>
								</Table.Row>
							{/each}
						</Table.Body>
					</Table.Root>
				{/if}
			</Card.Content>
		</Card.Root>

		<Card.Root>
			<Card.Header><Card.Title>{m.usage_attributesHeading()}</Card.Title></Card.Header>
			<Card.Content>
				<p class="text-muted-foreground mb-2 text-xs">
					{m.usage_attributesNote({ rows: formatCount(data.attributeSampleRows) })}
				</p>
				{#if data.attributes.length === 0}
					<p class="text-muted-foreground text-sm">{m.usage_empty()}</p>
				{:else}
					<Table.Root>
						<Table.Header>
							<Table.Row>
								<Table.Head>{m.usage_colSource()}</Table.Head>
								<Table.Head>{m.usage_colAttribute()}</Table.Head>
								<Table.Head class="text-right">{m.usage_colSampledBytes()}</Table.Head>
								<Table.Head class="text-right">{m.usage_colShare()}</Table.Head>
								<Table.Head class="text-right">{m.usage_colRows()}</Table.Head>
							</Table.Row>
						</Table.Header>
						<Table.Body>
							{#each data.attributes as a (`${a.signal}/${a.scope}/${a.key}`)}
								<Table.Row>
									<Table.Cell class="text-muted-foreground text-xs">{signalLabel(a.signal)} · {a.scope}</Table.Cell>
									<Table.Cell class="font-mono text-xs">{a.key}</Table.Cell>
									<Table.Cell class="text-right tabular-nums">{formatBytes(a.sampledBytes)}</Table.Cell>
									<Table.Cell class="text-right tabular-nums">{attributeShare(a)}%</Table.Cell>
									<Table.Cell class="text-right tabular-nums">{formatCount(a.sampledOccurrences)}</Table.Cell>
								</Table.Row>
							{/each}
						</Table.Body>
					</Table.Root>
				{/if}
			</Card.Content>
		</Card.Root>
	{/if}
</div>
