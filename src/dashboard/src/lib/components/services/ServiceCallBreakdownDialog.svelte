<script lang="ts">
	// The Services tab Map view's per-node drill-down - "what does this service call, and
	// how slow/erroring is each one," split into External calls (grouped by peer.service)
	// and Database calls (grouped by db.system/db.operation). Opened by clicking a node in
	// ServiceDependencyGraph.svelte; same open-derived-from-a-shared-selection-field,
	// onOpenChange-clears-it precedent as SpanDetailSheet.svelte reading
	// TraceDetailState.selectedSpanId - see ServicesState.selectedService's own remarks for
	// why the selection lives there rather than as local state on the graph component.
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import { formatMs, formatPercent } from '$lib/indexing/format';
	import { servicesContext } from '$lib/services/context';
	import { SERVICES_WINDOW_PRESETS } from '$lib/services/state.svelte';
	import { getServiceCallBreakdown, type ServiceCallBreakdown } from '$lib/services-api';
	import * as m from '$lib/paraglide/messages';

	const services = servicesContext.get();

	const open = $derived(services.selectedService !== null);

	let breakdown = $state.raw<ServiceCallBreakdown | null>(null);
	let loading = $state(false);
	let error = $state<string | null>(null);
	let abort: AbortController | null = null;

	// Same 2-tier warning/destructive error-rate escalation as ServicesTable's own
	// errorRateClass - not shared, since that one also lives beside a different
	// component's own imports and this is a small enough function to just repeat.
	function errorRateClass(errorRate: number): string {
		if (errorRate >= 0.05) return 'text-destructive font-medium';
		if (errorRate >= 0.01) return 'text-warning';
		return '';
	}

	// Re-fetches whenever the selected service (or the tab's own window) changes - not a
	// one-shot onMount the way PatternsModal's is, since this dialog stays mounted across
	// different nodes being clicked in a row rather than fully unmounting between opens
	// (Dialog.Root here is rendered once at the page level, unlike that modal's own
	// self-contained Dialog.Root+Trigger).
	$effect(() => {
		const service = services.selectedService;
		abort?.abort();

		if (!service) {
			breakdown = null;
			loading = false;
			return;
		}

		const minutes = SERVICES_WINDOW_PRESETS.find((p) => p.value === services.windowPreset)?.minutes ?? 15;
		const controller = new AbortController();
		abort = controller;
		loading = true;
		error = null;

		getServiceCallBreakdown(service, minutes, controller.signal)
			.then((result) => {
				if (controller.signal.aborted) return;
				breakdown = result;
			})
			.catch((err) => {
				if (controller.signal.aborted) return;
				error = err instanceof Error ? err.message : String(err);
			})
			.finally(() => {
				if (!controller.signal.aborted) loading = false;
			});

		return () => controller.abort();
	});

	function handleOpenChange(next: boolean): void {
		if (!next) services.selectedService = null;
	}
</script>

<Dialog.Root {open} onOpenChange={handleOpenChange}>
	<Dialog.Content class="sm:max-w-2xl">
		<Dialog.Header>
			<Dialog.Title>{services.selectedService}</Dialog.Title>
			<Dialog.Description>{m.serviceCallBreakdown_description()}</Dialog.Description>
		</Dialog.Header>
		<div class="max-h-[60vh] space-y-6 overflow-auto">
			{#if loading && !breakdown}
				<div class="flex justify-center py-12">
					<Spinner class="size-6" />
				</div>
			{:else if error}
				<Empty.Root>
					<Empty.Header>
						<Empty.Title>{m.serviceCallBreakdown_loadErrorTitle()}</Empty.Title>
						<Empty.Description>{error}</Empty.Description>
					</Empty.Header>
				</Empty.Root>
			{:else if breakdown}
				<div>
					<h3 class="text-muted-foreground mb-2 text-sm font-medium">{m.serviceCallBreakdown_externalCallsHeading()}</h3>
					{#if breakdown.externalCalls.length === 0}
						<p class="text-muted-foreground text-sm">{m.serviceCallBreakdown_externalEmptyDescription()}</p>
					{:else}
						<Table.Root>
							<Table.Header>
								<Table.Row>
									<Table.Head>{m.serviceCallBreakdown_targetColumn()}</Table.Head>
									<Table.Head class="text-right">{m.serviceCallBreakdown_callsColumn()}</Table.Head>
									<Table.Head class="text-right">{m.serviceCallBreakdown_errorRateColumn()}</Table.Head>
									<Table.Head class="text-right">{m.serviceCallBreakdown_p50Column()}</Table.Head>
									<Table.Head class="text-right">{m.serviceCallBreakdown_p95Column()}</Table.Head>
								</Table.Row>
							</Table.Header>
							<Table.Body>
								{#each breakdown.externalCalls as call (call.peerService)}
									<Table.Row>
										<Table.Cell class="font-medium">{call.peerService}</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{call.callCount}</Table.Cell>
										<Table.Cell class="text-right tabular-nums {errorRateClass(call.errorRate)}">
											{formatPercent(call.errorRate * 100)}
										</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{formatMs(call.p50DurationMs)}</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{formatMs(call.p95DurationMs)}</Table.Cell>
									</Table.Row>
								{/each}
							</Table.Body>
						</Table.Root>
					{/if}
				</div>
				<div>
					<h3 class="text-muted-foreground mb-2 text-sm font-medium">{m.serviceCallBreakdown_databaseCallsHeading()}</h3>
					{#if breakdown.databaseCalls.length === 0}
						<p class="text-muted-foreground text-sm">{m.serviceCallBreakdown_databaseEmptyDescription()}</p>
					{:else}
						<Table.Root>
							<Table.Header>
								<Table.Row>
									<Table.Head>{m.serviceCallBreakdown_dbSystemColumn()}</Table.Head>
									<Table.Head>{m.serviceCallBreakdown_dbOperationColumn()}</Table.Head>
									<Table.Head class="text-right">{m.serviceCallBreakdown_callsColumn()}</Table.Head>
									<Table.Head class="text-right">{m.serviceCallBreakdown_errorRateColumn()}</Table.Head>
									<Table.Head class="text-right">{m.serviceCallBreakdown_p50Column()}</Table.Head>
									<Table.Head class="text-right">{m.serviceCallBreakdown_p95Column()}</Table.Head>
								</Table.Row>
							</Table.Header>
							<Table.Body>
								{#each breakdown.databaseCalls as call (call.dbSystem + '/' + call.dbOperation)}
									<Table.Row>
										<Table.Cell class="font-medium">{call.dbSystem}</Table.Cell>
										<Table.Cell class="text-muted-foreground">
											{call.dbOperation || m.serviceCallBreakdown_unspecifiedOperation()}
										</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{call.callCount}</Table.Cell>
										<Table.Cell class="text-right tabular-nums {errorRateClass(call.errorRate)}">
											{formatPercent(call.errorRate * 100)}
										</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{formatMs(call.p50DurationMs)}</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{formatMs(call.p95DurationMs)}</Table.Cell>
									</Table.Row>
								{/each}
							</Table.Body>
						</Table.Root>
					{/if}
				</div>
			{/if}
		</div>
	</Dialog.Content>
</Dialog.Root>
