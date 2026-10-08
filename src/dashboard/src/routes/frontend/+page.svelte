<script lang="ts">
	// Frontend (ADR-0153): browser web vitals per service and the top JavaScript errors, from the
	// existing metrics and errors queries - the signal conventions are ADR-0151's.
	import { onDestroy, onMount } from 'svelte';
	import * as Select from '$lib/components/ui/select';
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import GaugeIcon from '@lucide/svelte/icons/gauge';
	import ChevronRightIcon from '@lucide/svelte/icons/chevron-right';
	import { FrontendState, WEB_VITALS, pageLoads, rateVital, type VitalCell, type WebVital } from '$lib/frontend/state.svelte';
	import { SERVICES_WINDOW_PRESETS, servicesWindowPresetLabel, type ServicesWindowPreset } from '$lib/services/state.svelte';
	import { formatCount } from '$lib/ingestion/format';
	import { formatDateTime } from '$lib/time/format';
	import { withBase } from '$lib/paths';
	import * as m from '$lib/paraglide/messages';

	const frontend = new FrontendState();

	onMount(() => void frontend.load());
	onDestroy(() => frontend.dispose());

	function vitalLabel(vital: WebVital): string {
		return { lcp: 'LCP', inp: 'INP', cls: 'CLS', fcp: 'FCP', ttfb: 'TTFB' }[vital];
	}

	function formatVital(vital: WebVital, value: number): string {
		if (vital === 'cls') return value.toFixed(2);
		return value >= 1000 ? `${(value / 1000).toFixed(2)} s` : `${Math.round(value)} ms`;
	}

	const RATING_CLASS = {
		good: 'text-emerald-600 dark:text-emerald-400',
		'needs-improvement': 'text-warning',
		poor: 'text-destructive'
	} as const;

	let expanded = $state<Record<string, boolean>>({});

	function toggle(service: string): void {
		expanded[service] = !expanded[service];
		if (expanded[service]) void frontend.loadRoutes(service);
	}

	const errorsHref = $derived(frontend.errorsHref());
</script>

<svelte:head>
	<title>{m.frontendPage_title()}</title>
</svelte:head>

{#snippet vitalCell(vital: WebVital, cell: VitalCell | undefined)}
	{#if cell}
		<div class="flex flex-col tabular-nums">
			<span class={RATING_CLASS[rateVital(vital, cell.p75)]}>{formatVital(vital, cell.p75)}</span>
			{#if cell.goodShare != null}
				<span class="text-muted-foreground text-xs">{m.frontendPage_goodShare({ percent: Math.round(cell.goodShare * 100) })}</span>
			{/if}
		</div>
	{:else}
		<span class="text-muted-foreground">—</span>
	{/if}
{/snippet}

<div class="flex h-full flex-col overflow-y-auto">
	<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
		<h1 class="text-sm font-medium">{m.frontendPage_heading()}</h1>
		<Select.Root type="single" value={frontend.windowPreset} onValueChange={(v) => v && frontend.setWindowPreset(v as ServicesWindowPreset)}>
			<Select.Trigger class="ml-auto w-auto">
				<ClockIcon data-icon="inline-start" />
				{servicesWindowPresetLabel(frontend.windowPreset)}
			</Select.Trigger>
			<Select.Content>
				{#each SERVICES_WINDOW_PRESETS as preset (preset.value)}
					<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
				{/each}
			</Select.Content>
		</Select.Root>
		<Button variant="outline" size="sm" onclick={() => frontend.load()} disabled={frontend.loading}>
			{#if frontend.loading}
				<Spinner class="size-4" data-icon="inline-start" />
			{:else}
				<RefreshCwIcon data-icon="inline-start" />
			{/if}
			{m.frontendPage_refresh()}
		</Button>
	</div>

	{#if frontend.error}
		<div class="flex flex-1 items-center justify-center"><p class="text-destructive text-sm">{frontend.error}</p></div>
	{:else if frontend.services === null}
		<div class="flex flex-1 items-center justify-center"><Spinner /></div>
	{:else if frontend.services.length === 0 && frontend.errors.length === 0}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Media><GaugeIcon class="text-muted-foreground size-8" /></Empty.Media>
				<Empty.Title>{m.frontendPage_emptyTitle()}</Empty.Title>
				<Empty.Description>{m.frontendPage_emptyDescription()}</Empty.Description>
			</Empty.Header>
			<Empty.Content>
				<a class="text-sm underline" href="https://github.com/aminparsa18/Flare.Net/blob/main/docs/how-to/send-browser-telemetry.md">
					{m.frontendPage_emptyLink()}
				</a>
			</Empty.Content>
		</Empty.Root>
	{:else}
		<section class="px-4 pt-4">
			<h2 class="text-sm font-semibold">{m.frontendPage_vitalsHeading()}</h2>
			<p class="text-muted-foreground text-xs">{m.frontendPage_vitalsDescription()}</p>
		</section>
		{#if frontend.services.length === 0}
			<p class="text-muted-foreground px-4 py-6 text-sm">{m.frontendPage_noVitals()}</p>
		{:else}
			<Table.Root class="mt-2">
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.frontendPage_serviceColumn()}</Table.Head>
						{#each WEB_VITALS as vital (vital)}
							<Table.Head>{vitalLabel(vital)}</Table.Head>
						{/each}
						<Table.Head class="text-right">{m.frontendPage_pageLoadsColumn()}</Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each frontend.services as row (row.service)}
						<Table.Row>
							<Table.Cell class="font-medium">
								<button type="button" class="flex items-center gap-1" aria-expanded={!!expanded[row.service]} title={m.frontendPage_toggleRoutes()} onclick={() => toggle(row.service)}>
									<ChevronRightIcon class={['size-4 transition-transform', expanded[row.service] && 'rotate-90']} />
									{row.service}
								</button>
							</Table.Cell>
							{#each WEB_VITALS as vital (vital)}
								<Table.Cell>{@render vitalCell(vital, row.cells[vital])}</Table.Cell>
							{/each}
							<Table.Cell class="text-muted-foreground text-right tabular-nums">
								{formatCount(pageLoads(row.cells))}
							</Table.Cell>
						</Table.Row>
						{#if expanded[row.service]}
							{@const routes = frontend.routes[row.service]}
							{#if routes == null}
								<Table.Row class="bg-muted/30"><Table.Cell colspan={WEB_VITALS.length + 2}><Spinner class="size-4" /></Table.Cell></Table.Row>
							{:else if routes.every((r) => r.route === '')}
								<Table.Row class="bg-muted/30">
									<Table.Cell colspan={WEB_VITALS.length + 2} class="text-muted-foreground pl-9 text-xs">{m.frontendPage_noRoutes()}</Table.Cell>
								</Table.Row>
							{:else}
								{#each routes as r (r.route)}
									<Table.Row class="bg-muted/30">
										<Table.Cell class="pl-9 font-mono text-xs">{r.route || m.frontendPage_noRoute()}</Table.Cell>
										{#each WEB_VITALS as vital (vital)}
											<Table.Cell>{@render vitalCell(vital, r.cells[vital])}</Table.Cell>
										{/each}
										<Table.Cell class="text-muted-foreground text-right tabular-nums">{formatCount(pageLoads(r.cells))}</Table.Cell>
									</Table.Row>
								{/each}
							{/if}
						{/if}
					{/each}
				</Table.Body>
			</Table.Root>
		{/if}

		<section class="flex items-center justify-between px-4 pt-6">
			<div>
				<h2 class="text-sm font-semibold">
					{m.frontendPage_errorsHeading()}
					{#if frontend.totalErrors > 0}<Badge variant="secondary" class="ml-1">{formatCount(frontend.totalErrors)}</Badge>{/if}
				</h2>
				<p class="text-muted-foreground text-xs">{m.frontendPage_errorsDescription()}</p>
			</div>
			{#if errorsHref}
				<Button variant="outline" size="sm" href={errorsHref}>{m.frontendPage_openErrors()}</Button>
			{/if}
		</section>
		{#if frontend.errors.length === 0}
			<p class="text-muted-foreground px-4 py-6 text-sm">{m.frontendPage_noErrors()}</p>
		{:else}
			<Table.Root class="mt-2">
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.frontendPage_errorColumn()}</Table.Head>
						<Table.Head>{m.frontendPage_serviceColumn()}</Table.Head>
						<Table.Head class="text-right">{m.frontendPage_countColumn()}</Table.Head>
						<Table.Head>{m.frontendPage_lastSeenColumn()}</Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each frontend.errors as group (group.exceptionType + '\n' + group.exceptionMessage)}
						<Table.Row>
							<Table.Cell class="max-w-xl">
								<a class="block truncate font-medium hover:underline" href={frontend.errorsHref(group) ?? withBase('/errors')}>
									{group.exceptionType}
								</a>
								<span class="text-muted-foreground block truncate text-xs">{group.exceptionMessage}</span>
							</Table.Cell>
							<Table.Cell class="text-muted-foreground">{group.affectedServices.join(', ')}</Table.Cell>
							<Table.Cell class="text-right tabular-nums">{formatCount(group.occurrenceCount)}</Table.Cell>
							<Table.Cell class="text-muted-foreground">{formatDateTime(group.lastSeen)}</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
		{/if}
	{/if}
</div>
