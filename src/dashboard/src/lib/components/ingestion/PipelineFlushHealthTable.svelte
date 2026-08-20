<script lang="ts">
	// Each *FlushWorker*'s own last-flush outcome (Planning.md v10) - a stuck/erroring
	// worker is exactly the "why did my logs stop showing up" case this whole item exists
	// to diagnose, and it's invisible from the stream table alone (a growing stream/PEL is
	// a symptom; this is the cause).
	//
	// Feedback: a row with consecutiveErrors=0 sitting next to a red lastError string read
	// as "currently broken" (e.g. Metrics · 1s ago · 0 errors · red "Connection refused") -
	// 0 consecutive errors is actually proof a *later* flush already succeeded. Replaced the
	// bare "Consecutive errors" count with a Status column (ingestion/health.ts's
	// computeFlushStatus) that separates "what's true right now" (Healthy/Retrying/Down/
	// Stuck) from "what happened historically" (Recovered, with the error text demoted to
	// muted history instead of a live alarm).
	import * as Table from '$lib/components/ui/table';
	import { ingestionContext } from '$lib/ingestion/context';
	import { formatAge, formatCount, secondsSince } from '$lib/ingestion/format';
	import { computeFlushStatus, type FlushStatusTone } from '$lib/ingestion/health';
	import CheckIcon from '@lucide/svelte/icons/check';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
	import CircleXIcon from '@lucide/svelte/icons/circle-x';
	import MinusIcon from '@lucide/svelte/icons/minus';

	const ingestion = ingestionContext.get();

	const workers = $derived(ingestion.pipeline?.flushWorkers ?? []);
	const streamBySignal = $derived(new Map((ingestion.pipeline?.streams ?? []).map((s) => [s.signal, s])));

	const TONE_TEXT_CLASS = {
		good: 'text-emerald-600 dark:text-emerald-400',
		default: 'text-muted-foreground',
		warning: 'text-warning',
		destructive: 'text-destructive'
	} satisfies Record<FlushStatusTone, string>;

	const TONE_ICON = {
		good: CheckIcon,
		default: MinusIcon,
		warning: TriangleAlertIcon,
		destructive: CircleXIcon
	} satisfies Record<FlushStatusTone, typeof CheckIcon>;
</script>

<div class="px-4 pb-4">
	<h2 class="mb-2 text-sm font-medium">Flush workers</h2>
	<Table.Root>
		<Table.Header>
			<Table.Row>
				<Table.Head>Signal</Table.Head>
				<Table.Head class="text-right">Last flush</Table.Head>
				<Table.Head class="text-right">Batch size</Table.Head>
				<Table.Head>Status</Table.Head>
				<Table.Head>Last error</Table.Head>
			</Table.Row>
		</Table.Header>
		<Table.Body>
			{#each workers as worker (worker.signal)}
				{@const status = computeFlushStatus(worker, streamBySignal.get(worker.signal))}
				{@const StatusIcon = TONE_ICON[status.tone]}
				<Table.Row>
					<Table.Cell class="font-medium">{worker.signal}</Table.Cell>
					<Table.Cell class="text-muted-foreground text-right tabular-nums">
						{worker.lastFlushAt ? formatAge(secondsSince(worker.lastFlushAt)) : 'never'}
					</Table.Cell>
					<Table.Cell class="text-right tabular-nums">
						{worker.lastBatchSize === null ? '—' : formatCount(worker.lastBatchSize)}
					</Table.Cell>
					<Table.Cell>
						<span class="flex items-center gap-1 {TONE_TEXT_CLASS[status.tone]}">
							<StatusIcon class="size-3.5 shrink-0" />
							{status.label}
						</span>
					</Table.Cell>
					<Table.Cell class="max-w-xs truncate font-mono text-xs" title={worker.lastError ?? undefined}>
						{#if worker.lastError && (status.key === 'down' || status.key === 'retrying')}
							<span class={TONE_TEXT_CLASS[status.tone]}>{worker.lastError}</span>
						{:else if worker.lastError}
							<!-- Demoted to muted history, not a live alarm - see this file's own
							     remarks. "recovered" is when the worker's own last *successful*
							     flush landed, the best evidence available for "since when has this
							     actually been fine". -->
							<span class="text-muted-foreground">
								{worker.lastError} · recovered {worker.lastFlushAt ? formatAge(secondsSince(worker.lastFlushAt)) : 'since'}
							</span>
						{:else}
							<span class="text-muted-foreground">—</span>
						{/if}
					</Table.Cell>
				</Table.Row>
			{/each}
		</Table.Body>
	</Table.Root>
</div>