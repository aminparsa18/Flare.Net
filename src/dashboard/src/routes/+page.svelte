<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import {
		API_BASE_URL,
		searchLogs,
		aggregateLogs,
		connectLiveTail,
		type LogEventDto,
		type LiveTailStatus
	} from '$lib/api';
	import * as Card from '$lib/components/ui/card';
	import { Badge, type BadgeVariant } from '$lib/components/ui/badge';
	import * as Table from '$lib/components/ui/table';
	import { Separator } from '$lib/components/ui/separator';
	import * as Empty from '$lib/components/ui/empty';
	import * as Alert from '$lib/components/ui/alert';
	import { Spinner } from '$lib/components/ui/spinner';
	import DatabaseIcon from '@lucide/svelte/icons/database';
	import ActivityIcon from '@lucide/svelte/icons/activity';
	import RadioIcon from '@lucide/svelte/icons/radio';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';

	type FetchStatus = 'loading' | 'ok' | 'error';

	const MAX_ROWS = 50;
	const VISIBLE_ROWS = 20;

	let searchStatus = $state<FetchStatus>('loading');
	let searchError = $state<string | null>(null);

	let aggregateStatus = $state<FetchStatus>('loading');
	let aggregateError = $state<string | null>(null);
	let eventsLastHour = $state<number | null>(null);

	let liveStatus = $state<LiveTailStatus>('connecting');
	let liveError = $state<string | null>(null);
	let liveEventCount = $state(0);
	let droppedCount = $state(0);

	let events = $state<LogEventDto[]>([]);

	let socket: WebSocket | null = null;

	function formatTime(iso: string): string {
		return new Date(iso).toLocaleTimeString(undefined, { hour12: false });
	}

	function severityVariant(severityNumber: number): BadgeVariant {
		if (severityNumber >= 17) return 'destructive'; // Error / Fatal
		if (severityNumber >= 9) return 'secondary'; // Info / Warn
		return 'outline'; // Trace / Debug
	}

	function upsertEvent(event: LogEventDto) {
		events = [event, ...events.filter((e) => e.eventId !== event.eventId)].slice(0, MAX_ROWS);
	}

	onMount(() => {
		searchLogs({ pageSize: VISIBLE_ROWS })
			.then((res) => {
				events = res.events;
				searchStatus = 'ok';
			})
			.catch((err) => {
				searchStatus = 'error';
				searchError = err instanceof Error ? err.message : String(err);
			});

		aggregateLogs({ bucketWidthSeconds: 300 })
			.then((res) => {
				eventsLastHour = res.buckets.reduce((sum, b) => sum + b.count, 0);
				aggregateStatus = 'ok';
			})
			.catch((err) => {
				aggregateStatus = 'error';
				aggregateError = err instanceof Error ? err.message : String(err);
			});

		socket = connectLiveTail(
			{},
			{
				onStatusChange: (status) => (liveStatus = status),
				onEvent: (event) => {
					liveEventCount += 1;
					upsertEvent(event);
				},
				onDropped: (count) => (droppedCount += count),
				onError: (error) => (liveError = error)
			}
		);
	});

	onDestroy(() => {
		socket?.close();
	});

	const liveStatusVariant: Record<LiveTailStatus, BadgeVariant> = {
		connecting: 'outline',
		open: 'secondary',
		closed: 'destructive',
		error: 'destructive'
	};
</script>

<svelte:head>
	<title>Flare — Overview</title>
</svelte:head>

<div class="mx-auto flex max-w-5xl flex-col gap-6 p-8">
	<div class="flex flex-col gap-1">
		<h1 class="text-2xl font-semibold">Overview</h1>
		<p class="text-muted-foreground text-sm">
			Verifying the pipeline against <code class="text-foreground">{API_BASE_URL}</code>
		</p>
	</div>

	<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
		<Card.Root>
			<Card.Header>
				<Card.Title class="flex items-center gap-2">
					<DatabaseIcon class="text-muted-foreground size-4" />
					Query API
				</Card.Title>
				<Card.Description>POST /api/logs/search</Card.Description>
			</Card.Header>
			<Card.Content>
				{#if searchStatus === 'loading'}
					<Spinner />
				{:else if searchStatus === 'ok'}
					<div class="flex items-center gap-2">
						<Badge variant="secondary">Reachable</Badge>
						<span class="text-muted-foreground text-sm">{events.length} loaded</span>
					</div>
				{:else}
					<Badge variant="destructive">Unreachable</Badge>
				{/if}
			</Card.Content>
		</Card.Root>

		<Card.Root>
			<Card.Header>
				<Card.Title class="flex items-center gap-2">
					<ActivityIcon class="text-muted-foreground size-4" />
					Volume (last hour)
				</Card.Title>
				<Card.Description>POST /api/logs/aggregate</Card.Description>
			</Card.Header>
			<Card.Content>
				{#if aggregateStatus === 'loading'}
					<Spinner />
				{:else if aggregateStatus === 'ok'}
					<span class="text-2xl font-semibold tabular-nums">{eventsLastHour}</span>
					<span class="text-muted-foreground text-sm">events</span>
				{:else}
					<Badge variant="destructive">Unreachable</Badge>
				{/if}
			</Card.Content>
		</Card.Root>

		<Card.Root>
			<Card.Header>
				<Card.Title class="flex items-center gap-2">
					<RadioIcon class="text-muted-foreground size-4" />
					Live tail
				</Card.Title>
				<Card.Description>GET /api/logs/tail (WebSocket)</Card.Description>
			</Card.Header>
			<Card.Content>
				<div class="flex items-center gap-2">
					<Badge variant={liveStatusVariant[liveStatus]}>{liveStatus}</Badge>
					<span class="text-muted-foreground text-sm">{liveEventCount} received</span>
				</div>
				{#if droppedCount > 0}
					<p class="text-muted-foreground mt-1 text-xs">{droppedCount} dropped (slow reader)</p>
				{/if}
			</Card.Content>
		</Card.Root>
	</div>

	{#if searchError || aggregateError || liveError}
		<Alert.Root variant="destructive">
			<TriangleAlertIcon />
			<Alert.Title>Something didn't wire up</Alert.Title>
			<Alert.Description>
				{#if searchError}<p>Search: {searchError}</p>{/if}
				{#if aggregateError}<p>Aggregate: {aggregateError}</p>{/if}
				{#if liveError}<p>Live tail: {liveError}</p>{/if}
			</Alert.Description>
		</Alert.Root>
	{/if}

	<Separator />

	<Card.Root>
		<Card.Header>
			<Card.Title>Recent events</Card.Title>
			<Card.Description>
				Seeded from the last search, updated live as new events arrive
			</Card.Description>
		</Card.Header>
		<Card.Content>
			{#if events.length === 0}
				<Empty.Root>
					<Empty.Header>
						<Empty.Title>No events yet</Empty.Title>
						<Empty.Description>
							Point a logger's OTLP exporter at Flare.Ingest and they'll show up here.
						</Empty.Description>
					</Empty.Header>
				</Empty.Root>
			{:else}
				<Table.Root>
					<Table.Header>
						<Table.Row>
							<Table.Head>Time</Table.Head>
							<Table.Head>Level</Table.Head>
							<Table.Head>Service</Table.Head>
							<Table.Head>Message</Table.Head>
						</Table.Row>
					</Table.Header>
					<Table.Body>
						{#each events.slice(0, VISIBLE_ROWS) as event (event.eventId)}
							<Table.Row>
								<Table.Cell class="text-muted-foreground font-mono text-xs whitespace-nowrap"
									>{formatTime(event.timestamp)}</Table.Cell
								>
								<Table.Cell
									><Badge variant={severityVariant(event.severityNumber)}
										>{event.severityText || '—'}</Badge
									></Table.Cell
								>
								<Table.Cell class="whitespace-nowrap">{event.serviceName || '—'}</Table.Cell>
								<Table.Cell class="max-w-md truncate">{event.body}</Table.Cell>
							</Table.Row>
						{/each}
					</Table.Body>
				</Table.Root>
			{/if}
		</Card.Content>
	</Card.Root>
</div>
