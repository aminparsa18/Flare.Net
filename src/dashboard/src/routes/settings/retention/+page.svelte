<script lang="ts">
	// Data retention (ADR-0143/0144/0145): per-signal TTLs, the cold tier, per-resource rules.
	// ClickHouse applies a change asynchronously, so while one is pending the page polls.
	import { onDestroy, onMount } from 'svelte';
	import * as Card from '$lib/components/ui/card';
	import { Spinner } from '$lib/components/ui/spinner';
	import RetentionSignalCard from '$lib/components/retention/RetentionSignalCard.svelte';
	import { getRetention, setRetention, type RetentionResponse, type RetentionRule } from '$lib/retention-api';
	import { formatBytes } from '$lib/ingestion/format';
	import * as m from '$lib/paraglide/messages';

	let data = $state.raw<RetentionResponse | null>(null);
	let loadError = $state<string | null>(null);
	let saveError = $state<{ signal: string; message: string } | null>(null);
	let timer: ReturnType<typeof setTimeout> | undefined;

	const busy = $derived(data?.signals.some((s) => s.status === 'pending') ?? false);

	async function load() {
		try {
			data = await getRetention();
			loadError = null;
		} catch (err) {
			loadError = err instanceof Error ? err.message : String(err);
		}
		clearTimeout(timer);
		if (data?.signals.some((s) => s.status === 'pending')) timer = setTimeout(load, 2000);
	}

	async function save(signal: string, change: { days: number; coldAfterDays: number; rules: RetentionRule[] }) {
		saveError = null;
		try {
			await setRetention({ signal, ...change });
		} catch (err) {
			saveError = { signal, message: err instanceof Error ? err.message : String(err) };
			return;
		}
		await load();
	}

	onMount(() => void load());
	onDestroy(() => clearTimeout(timer));
</script>

<svelte:head>
	<title>{m.retentionPage_title()}</title>
</svelte:head>

<div class="flex flex-col gap-4 p-4">
	<div>
		<h1 class="text-lg font-semibold">{m.retentionPage_heading()}</h1>
		<p class="text-muted-foreground text-sm">{m.retentionPage_intro()}</p>
	</div>

	{#if loadError}
		<p class="text-destructive text-sm">{loadError}</p>
	{/if}

	{#if !data}
		{#if !loadError}
			<div class="flex h-32 items-center justify-center"><Spinner /></div>
		{/if}
	{:else}
		<Card.Root>
			<Card.Header><Card.Title>{m.retention_coldHeading()}</Card.Title></Card.Header>
			<Card.Content class="text-sm">
				{#if data.coldStorage.available}
					<ul class="flex flex-col gap-1">
						{#each data.coldStorage.disks as disk (disk.name)}
							<li>
								<span class="font-medium">{disk.name}</span>
								<span class="text-muted-foreground">
									· {disk.type === 'ObjectStorage' ? m.retention_diskObject() : m.retention_diskLocal()} · {m.retention_diskFree({
										free: formatBytes(disk.freeBytes),
										total: formatBytes(disk.totalBytes)
									})}
								</span>
							</li>
						{/each}
					</ul>
				{:else}
					<p class="text-muted-foreground">{m.retention_coldUnavailable()}</p>
				{/if}
			</Card.Content>
		</Card.Root>

		{#each data.signals as signal (signal.signal + (signal.transactionId ?? '') + (signal.status ?? ''))}
			<RetentionSignalCard
				retention={signal}
				coldAvailable={data.coldStorage.available}
				{busy}
				error={saveError?.signal === signal.signal ? saveError.message : null}
				onsave={(change) => save(signal.signal, change)}
			/>
		{/each}
	{/if}
</div>
