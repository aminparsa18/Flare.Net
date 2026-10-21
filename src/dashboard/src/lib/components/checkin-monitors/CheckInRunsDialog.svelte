<script lang="ts">
	// Recent runs of one check-in monitor, paired from its start and finish pings (CheckInRunHistory server-side).
	// See docs-internal/adr/0260-cron-job-checkins.md.
	import * as Dialog from '#lib/components/ui/dialog/index.ts';
	import * as Table from '#lib/components/ui/table/index.ts';
	import { Badge } from '#lib/components/ui/badge/index.ts';
	import { Spinner } from '#lib/components/ui/spinner/index.ts';
	import { checkInMonitorsContext } from '#lib/checkin-monitors/context.ts';
	import { formatRunDuration, type CheckInRun, type CheckInRunOutcome } from '#lib/checkin-monitors-api.ts';
	import { formatDateTime } from '#lib/time/format.ts';
	import * as m from '#lib/paraglide/messages.js';

	const monitors = checkInMonitorsContext.get();

	const open = $derived(monitors.runsTarget !== null);

	const OUTCOME_LABELS: Record<CheckInRunOutcome, () => string> = {
		Ok: () => m.checkin_runOk(),
		Failed: () => m.checkin_runFailed(),
		Running: () => m.checkin_runRunning(),
		Unfinished: () => m.checkin_runUnfinished()
	};

	function badgeVariant(outcome: CheckInRunOutcome): 'secondary' | 'destructive' | 'outline' {
		if (outcome === 'Failed') return 'destructive';
		return outcome === 'Ok' ? 'secondary' : 'outline';
	}

	function when(run: CheckInRun): string {
		const at = run.startedAt ?? run.finishedAt;
		return at ? formatDateTime(at) : '';
	}

	/** Runs that have a duration, for the summary line. */
	const timed = $derived(monitors.runs.filter((r) => r.durationSeconds !== null));
	const failures = $derived(monitors.runs.filter((r) => r.outcome === 'Failed').length);
	const average = $derived(timed.length ? timed.reduce((sum, r) => sum + (r.durationSeconds ?? 0), 0) / timed.length : null);
</script>

<Dialog.Root {open} onOpenChange={(next) => !next && monitors.closeRuns()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-3xl">
		<Dialog.Header>
			<Dialog.Title>{m.checkin_runsTitle({ name: monitors.runsTarget?.name ?? '' })}</Dialog.Title>
			<Dialog.Description>{m.checkin_runsDescription()}</Dialog.Description>
		</Dialog.Header>

		{#if monitors.runsLoading}
			<div class="flex justify-center py-8"><Spinner /></div>
		{:else if monitors.runsError}
			<p class="text-destructive text-sm">{monitors.runsError}</p>
		{:else if monitors.runs.length === 0}
			<p class="text-muted-foreground py-6 text-center text-sm">{m.checkin_runsEmpty()}</p>
		{:else}
			<p class="text-muted-foreground text-xs">
				{m.checkin_runsSummary({
					count: monitors.runs.length,
					failures,
					average: average === null ? '-' : formatRunDuration(average)
				})}
			</p>
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.checkin_runColStarted()}</Table.Head>
						<Table.Head>{m.checkin_runColDuration()}</Table.Head>
						<Table.Head>{m.checkin_runColOutcome()}</Table.Head>
						<Table.Head>{m.checkin_runColMessage()}</Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each monitors.runs as run, i (i)}
						<Table.Row>
							<Table.Cell class="text-xs">{when(run)}</Table.Cell>
							<Table.Cell class="font-mono text-xs">
								{run.durationSeconds === null ? '-' : formatRunDuration(run.durationSeconds)}
							</Table.Cell>
							<Table.Cell><Badge variant={badgeVariant(run.outcome)}>{OUTCOME_LABELS[run.outcome]()}</Badge></Table.Cell>
							<Table.Cell class="text-muted-foreground max-w-xs truncate text-xs" title={run.message}>{run.message}</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
		{/if}
	</Dialog.Content>
</Dialog.Root>
