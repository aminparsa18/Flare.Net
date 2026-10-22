<script lang="ts">
	// Per-statement database view: normalized statements across a window with call rate,
	// error count, p95 and total time. Complements the N+1 tab (repeats inside one trace)
	// and the span-duration comparison (one span against its peers). See DbStatementQueryBuilder.cs.
	import { onMount } from 'svelte';
	import { withBase } from '#lib/paths.ts';
	import { getDbStatements, type DbStatementRow, type DbStatementSort } from '#lib/db-statements-api.ts';
	import { SERVICES_WINDOW_PRESETS, servicesWindowPresetLabel, type ServicesWindowPreset } from '#lib/services/state.svelte.ts';
	import TracesViewTabs from '#lib/components/traces/TracesViewTabs.svelte';
	import * as Select from '#lib/components/ui/select/index.ts';
	import * as Table from '#lib/components/ui/table/index.ts';
	import { Input } from '#lib/components/ui/input/index.ts';
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Spinner } from '#lib/components/ui/spinner/index.ts';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import * as m from '#lib/paraglide/messages.js';

	let windowPreset = $state<ServicesWindowPreset>('1h');
	let service = $state('');
	let dbSystem = $state('');
	let sortBy = $state<DbStatementSort>('totalTime');
	let statements = $state<DbStatementRow[]>([]);
	let loading = $state(false);
	let error = $state<string | null>(null);
	let loaded = $state(false);
	let controller: AbortController | undefined;

	const sortOptions: { value: DbStatementSort; label: () => string }[] = [
		{ value: 'totalTime', label: m.dbStatementsPage_sortTotalTime },
		{ value: 'p95', label: m.dbStatementsPage_sortP95 },
		{ value: 'calls', label: m.dbStatementsPage_sortCalls },
		{ value: 'errors', label: m.dbStatementsPage_sortErrors }
	];

	async function load() {
		controller?.abort();
		const mine = (controller = new AbortController());
		loading = true;
		error = null;
		try {
			const minutes = SERVICES_WINDOW_PRESETS.find((p) => p.value === windowPreset)?.minutes ?? 60;
			const response = await getDbStatements(minutes, service.trim(), dbSystem.trim(), sortBy, mine.signal);
			statements = response.statements;
			loaded = true;
		} catch (e) {
			if (mine.signal.aborted) return;
			error = e instanceof Error ? e.message : String(e);
		} finally {
			if (controller === mine) loading = false;
		}
	}

	function formatMs(ms: number): string {
		return ms >= 100 ? ms.toFixed(0) : ms.toFixed(1);
	}

	function formatRate(perMinute: number): string {
		return perMinute >= 10 ? perMinute.toFixed(0) : perMinute.toFixed(2);
	}

	onMount(() => {
		void load();
		return () => controller?.abort();
	});
</script>

<svelte:head>
	<title>{m.dbStatementsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
		<TracesViewTabs activeTab="database" />
		<Input class="ml-auto w-48" placeholder={m.dbStatementsPage_servicePlaceholder()} bind:value={service} onkeydown={(e) => e.key === 'Enter' && load()} />
		<Input class="w-36" placeholder={m.dbStatementsPage_systemPlaceholder()} bind:value={dbSystem} onkeydown={(e) => e.key === 'Enter' && load()} />
		<Select.Root
			type="single"
			value={sortBy}
			onValueChange={(v) => {
				if (!v) return;
				sortBy = v as DbStatementSort;
				void load();
			}}
		>
			<Select.Trigger class="w-auto">
				{sortOptions.find((o) => o.value === sortBy)?.label()}
			</Select.Trigger>
			<Select.Content>
				{#each sortOptions as option (option.value)}
					<Select.Item value={option.value} label={option.label()} />
				{/each}
			</Select.Content>
		</Select.Root>
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
			{m.dbStatementsPage_refresh()}
		</Button>
	</div>

	<p class="text-muted-foreground px-4 pt-3 text-sm">{m.dbStatementsPage_intro()}</p>

	{#if error}
		<p class="text-destructive px-4 py-3 text-sm">{error}</p>
	{:else if loaded && statements.length === 0}
		<p class="text-muted-foreground px-4 py-6 text-sm">{m.dbStatementsPage_empty()}</p>
	{:else if statements.length > 0}
		<div class="p-4">
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.dbStatementsPage_colService()}</Table.Head>
						<Table.Head>{m.dbStatementsPage_colStatement()}</Table.Head>
						<Table.Head class="text-right">{m.dbStatementsPage_colCalls()}</Table.Head>
						<Table.Head class="text-right">{m.dbStatementsPage_colRate()}</Table.Head>
						<Table.Head class="text-right">{m.dbStatementsPage_colErrors()}</Table.Head>
						<Table.Head class="text-right">{m.dbStatementsPage_colAvgMs()}</Table.Head>
						<Table.Head class="text-right">{m.dbStatementsPage_colP95Ms()}</Table.Head>
						<Table.Head class="text-right">{m.dbStatementsPage_colTotalMs()}</Table.Head>
						<Table.Head></Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each statements as s (s.serviceName + '\u0000' + s.dbSystem + '\u0000' + s.statement)}
						<Table.Row>
							<Table.Cell class="whitespace-nowrap">{s.serviceName || '—'}</Table.Cell>
							<Table.Cell class="max-w-xl truncate font-mono text-xs" title={s.statement}>
								<span class="text-muted-foreground">{s.dbSystem}</span>
								{s.statement}
							</Table.Cell>
							<Table.Cell class="text-right tabular-nums">{s.callCount}</Table.Cell>
							<Table.Cell class="text-right tabular-nums">{formatRate(s.callsPerMinute)}</Table.Cell>
							<Table.Cell class={s.errorCount > 0 ? 'text-destructive text-right tabular-nums' : 'text-right tabular-nums'}>{s.errorCount}</Table.Cell>
							<Table.Cell class="text-right tabular-nums">{formatMs(s.avgDurationMs)}</Table.Cell>
							<Table.Cell class="text-right tabular-nums">{formatMs(s.p95DurationMs)}</Table.Cell>
							<Table.Cell class="text-right tabular-nums">{s.totalDurationMs.toFixed(0)}</Table.Cell>
							<Table.Cell class="text-right">
								<a class="text-primary text-sm hover:underline" href={withBase(`/traces/${s.exampleTraceId}`)}>{m.dbStatementsPage_viewTrace()}</a>
							</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
		</div>
	{/if}
</div>
