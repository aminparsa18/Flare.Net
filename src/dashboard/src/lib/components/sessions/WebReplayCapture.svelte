<script lang="ts">
	// The console output and network requests the browser recorder captured (ADR-0266), listed under the player. A row opens the
	// recording at its moment; rows up to the current position are drawn normally and later ones dimmed, so the list follows playback.
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Badge } from '#lib/components/ui/badge/index.ts';
	import type { WebReplayState } from '#lib/app-sessions/web-replay.svelte.ts';
	import { formatOffset } from '#lib/app-sessions/timeline.svelte.ts';
	import * as m from '#lib/paraglide/messages.js';

	let { replay }: { replay: WebReplayState } = $props();

	/** Rows drawn at once; the recorder may have captured thousands. */
	const MAX_ROWS = 300;

	let tab = $state<'console' | 'network'>('console');
	// Opens on whichever kind the recording has.
	$effect.pre(() => {
		if (replay.consoleEntries.length === 0 && replay.networkEntries.length > 0) tab = 'network';
	});

	const consoleRows = $derived(replay.consoleEntries.slice(0, MAX_ROWS));
	const networkRows = $derived(replay.networkEntries.slice(0, MAX_ROWS));

	function levelVariant(level: string): 'destructive' | 'secondary' | 'outline' {
		return level === 'error' || level === 'assert' ? 'destructive' : level === 'warn' ? 'secondary' : 'outline';
	}

	function statusClass(status: number | undefined): string {
		if (status === undefined) return 'text-muted-foreground';
		return status >= 400 ? 'text-destructive' : 'text-foreground';
	}
</script>

<div class="mt-3 rounded-md border">
	<div class="flex items-center gap-1 border-b p-1">
		{#if replay.consoleEntries.length > 0}
			<Button variant={tab === 'console' ? 'secondary' : 'ghost'} size="xs" onclick={() => (tab = 'console')}>
				{m.sessionsPage_webReplay_console()} ({replay.consoleEntries.length})
			</Button>
		{/if}
		{#if replay.networkEntries.length > 0}
			<Button variant={tab === 'network' ? 'secondary' : 'ghost'} size="xs" onclick={() => (tab = 'network')}>
				{m.sessionsPage_webReplay_network()} ({replay.networkEntries.length})
			</Button>
		{/if}
	</div>
	<ul class="max-h-64 overflow-y-auto font-mono text-xs">
		{#if tab === 'console'}
			{#each consoleRows as entry, i (i)}
				<li class={entry.unixMs > replay.currentUnixMs ? 'opacity-50' : ''}>
					<button
						type="button"
						class="hover:bg-muted flex w-full items-baseline gap-2 px-2 py-1 text-left"
						onclick={() => replay.seekToUnixMs(entry.unixMs)}
					>
						<span class="text-muted-foreground w-20 shrink-0 tabular-nums">{formatOffset(entry.unixMs - replay.startUnixMs)}</span>
						<Badge variant={levelVariant(entry.level)} class="shrink-0">{entry.level}</Badge>
						<span class="min-w-0 flex-1 break-words whitespace-pre-wrap">{entry.text}</span>
					</button>
				</li>
			{/each}
		{:else}
			{#each networkRows as entry, i (i)}
				<li class={entry.unixMs > replay.currentUnixMs ? 'opacity-50' : ''}>
					<button
						type="button"
						class="hover:bg-muted flex w-full items-baseline gap-2 px-2 py-1 text-left"
						onclick={() => replay.seekToUnixMs(entry.unixMs)}
					>
						<span class="text-muted-foreground w-20 shrink-0 tabular-nums">{formatOffset(entry.unixMs - replay.startUnixMs)}</span>
						<span class="w-10 shrink-0 tabular-nums {statusClass(entry.status)}">{entry.status ?? '–'}</span>
						<span class="min-w-0 flex-1 break-all">{entry.url}</span>
						{#if entry.durationMs !== undefined}
							<span class="text-muted-foreground shrink-0 tabular-nums">{entry.durationMs} ms</span>
						{/if}
					</button>
				</li>
			{/each}
		{/if}
	</ul>
	{#if (tab === 'console' ? replay.consoleEntries.length : replay.networkEntries.length) > MAX_ROWS}
		<p class="text-muted-foreground border-t px-2 py-1 text-xs">{m.sessionsPage_webReplay_captureTruncated({ count: MAX_ROWS })}</p>
	{/if}
</div>
