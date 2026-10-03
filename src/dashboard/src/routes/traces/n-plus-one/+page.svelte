<script lang="ts">
	// N+1 worst offenders: database statements one parent span repeated within a trace,
	// aggregated per service over a window. The per-trace badge lives in TraceWaterfall;
	// this is the "where should I look first" list. See NPlusOneQueryBuilder.cs.
	import { onMount } from 'svelte';
	import { withBase } from '$lib/paths';
	import { getNPlusOneOffenders, type NPlusOneOffender } from '$lib/n-plus-one-api';
	import { SERVICES_WINDOW_PRESETS, servicesWindowPresetLabel, type ServicesWindowPreset } from '$lib/services/state.svelte';
	import { DEFAULT_N_PLUS_ONE_MIN_REPEATS } from '$lib/traces/n-plus-one';
	import TracesViewTabs from '$lib/components/traces/TracesViewTabs.svelte';
	import * as Select from '$lib/components/ui/select';
	import * as Table from '$lib/components/ui/table';
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import * as m from '$lib/paraglide/messages';

	let windowPreset = $state<ServicesWindowPreset>('1h');
	let service = $state('');
	let minRepeats = $state(DEFAULT_N_PLUS_ONE_MIN_REPEATS);
	let offenders = $state<NPlusOneOffender[]>([]);
	let loading = $state(false);
	let error = $state<string | null>(null);
	let loaded = $state(false);
	let controller: AbortController | undefined;

	async function load() {
		controller?.abort();
		const mine = (controller = new AbortController());
		loading = true;
		error = null;
		try {
			const minutes = SERVICES_WINDOW_PRESETS.find((p) => p.value === windowPreset)?.minutes ?? 60;
			const response = await getNPlusOneOffenders(minutes, service.trim(), minRepeats, mine.signal);
			offenders = response.offenders;
			loaded = true;
		} catch (e) {
			if (mine.signal.aborted) return;
			error = e instanceof Error ? e.message : String(e);
		} finally {
			if (controller === mine) loading = false;
		}
	}

	onMount(() => {
		void load();
		return () => controller?.abort();
	});
</script>

<svelte:head>
	<title>{m.nPlusOnePage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
		<TracesViewTabs activeTab="n-plus-one" />
		<Input class="ml-auto w-48" placeholder={m.nPlusOnePage_servicePlaceholder()} bind:value={service} onkeydown={(e) => e.key === 'Enter' && load()} />
		<Input
			type="number"
			class="w-24"
			min="2"
			title={m.nPlusOnePage_minRepeats()}
			aria-label={m.nPlusOnePage_minRepeats()}
			bind:value={minRepeats}
			onkeydown={(e) => e.key === 'Enter' && load()}
		/>
		<Select.Root
			type="single"
			value={windowPreset}
			onValueChange={(v) => {
				if (!v) return;
				windowPreset = v as ServicesWindowPreset;
				void load();
			}}
		>
			<Select.Trigger class="w-auto">
				<ClockIcon data-icon="inline-start" />
				{servicesWindowPresetLabel(windowPreset)}
			</Select.Trigger>
			<Select.Content>
				{#each SERVICES_WINDOW_PRESETS as preset (preset.value)}
					<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
				{/each}
			</Select.Content>
		</Select.Root>
		<Button size="sm" onclick={load} disabled={loading}>
			{#if loading}
				<Spinner class="size-4" data-icon="inline-start" />
			{:else}
				<RefreshCwIcon data-icon="inline-start" />
			{/if}
			{m.nPlusOnePage_refresh()}
		</Button>
	</div>

	<p class="text-muted-foreground px-4 pt-3 text-sm">{m.nPlusOnePage_intro()}</p>

	{#if error}
		<p class="text-destructive px-4 py-3 text-sm">{error}</p>
	{:else if loaded && offenders.length === 0}
		<p class="text-muted-foreground px-4 py-6 text-sm">{m.nPlusOnePage_empty()}</p>
	{:else if offenders.length > 0}
		<div class="p-4">
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.nPlusOnePage_colService()}</Table.Head>
						<Table.Head>{m.nPlusOnePage_colStatement()}</Table.Head>
						<Table.Head class="text-right">{m.nPlusOnePage_colTraces()}</Table.Head>
						<Table.Head class="text-right">{m.nPlusOnePage_colMaxRepeats()}</Table.Head>
						<Table.Head class="text-right">{m.nPlusOnePage_colTotalMs()}</Table.Head>
						<Table.Head></Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each offenders as o (o.serviceName + '\u0000' + o.statement)}
						<Table.Row>
							<Table.Cell class="whitespace-nowrap">{o.serviceName || '—'}</Table.Cell>
							<Table.Cell class="max-w-xl truncate font-mono text-xs" title={o.statement}>{o.statement}</Table.Cell>
							<Table.Cell class="text-right tabular-nums">{o.traceCount}</Table.Cell>
							<Table.Cell class="text-right tabular-nums">{o.maxRepeats}</Table.Cell>
							<Table.Cell class="text-right tabular-nums">{o.totalDurationMs.toFixed(0)}</Table.Cell>
							<Table.Cell class="text-right">
								<a class="text-primary text-sm hover:underline" href={withBase(`/traces/${o.exampleTraceId}`)}>{m.nPlusOnePage_viewTrace()}</a>
							</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
		</div>
	{/if}
</div>
