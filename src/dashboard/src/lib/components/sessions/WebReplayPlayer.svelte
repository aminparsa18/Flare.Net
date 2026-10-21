<script lang="ts">
	// Browser session replay (ADR-0264): rrweb's recorded DOM events played back in the page, with the span the user was in at
	// the current moment. Renders nothing when the session has no recording. The events are fetched, and rrweb imported,
	// only when "Watch recording" is pressed.
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Badge } from '#lib/components/ui/badge/index.ts';
	import WebReplayCapture from '#lib/components/sessions/WebReplayCapture.svelte';
	import { Spinner } from '#lib/components/ui/spinner/index.ts';
	import { Switch } from '#lib/components/ui/switch/index.ts';
	import PlayIcon from '@lucide/svelte/icons/play';
	import PauseIcon from '@lucide/svelte/icons/pause';
	import MonitorPlayIcon from '@lucide/svelte/icons/monitor-play';
	import { REPLAY_SPEEDS, type WebReplayState } from '#lib/app-sessions/web-replay.svelte.ts';
	import { eventAt } from '#lib/app-sessions/replay.svelte.ts';
	import { formatOffset } from '#lib/app-sessions/timeline.svelte.ts';
	import type { AppSessionTimelineEvent } from '#lib/app-sessions-api.ts';
	import { formatBytes } from '#lib/ingestion/format.ts';
	import * as m from '#lib/paraglide/messages.js';

	let { replay, events }: { replay: WebReplayState; events: AppSessionTimelineEvent[] } = $props();

	let root = $state<HTMLElement>();
	let measure = $state<HTMLElement>();

	const nearest = $derived(replay.ready && events.length > 0 ? eventAt(events, replay.currentUnixMs) : undefined);

	function start(): void {
		if (root && measure) void replay.start(root, measure);
	}
</script>

{#if replay.hasReplay}
	<section class="mt-3 mb-4 rounded-md border p-3">
		<div class="mb-2 flex flex-wrap items-center gap-2">
			<h2 class="text-sm font-medium">{m.sessionsPage_webReplay_heading()}</h2>
			<span class="text-muted-foreground text-xs">
				{m.sessionsPage_webReplay_summary({ count: replay.replay?.chunks.length ?? 0, size: formatBytes(replay.totalBytes) })}
			</span>
			{#if replay.ready}
				<span class="text-muted-foreground font-mono text-xs tabular-nums">{formatOffset(replay.elapsedMs)}</span>
			{/if}
			{#if nearest}
				<Badge variant={nearest.isError ? 'destructive' : 'outline'} title={m.sessionsPage_webReplay_spanNow()}>{nearest.name}</Badge>
			{/if}
		</div>

		{#if !replay.ready}
			<div class="bg-muted flex min-h-32 flex-col items-center justify-center gap-2 rounded-md p-4">
				{#if replay.loading}
					<Spinner />
					<p class="text-muted-foreground text-xs">
						{m.sessionsPage_webReplay_loading({ loaded: replay.loadedChunks, total: replay.replay?.chunks.length ?? 0 })}
					</p>
				{:else}
					<Button variant="outline" size="sm" onclick={start}>
						<MonitorPlayIcon data-icon="inline-start" />
						{m.sessionsPage_webReplay_load()}
					</Button>
					{#if replay.isLarge}
						<p class="text-warning text-xs">{m.sessionsPage_webReplay_largeWarning()}</p>
					{/if}
					<p class="text-muted-foreground text-xs">{m.sessionsPage_webReplay_privacy()}</p>
				{/if}
			</div>
		{/if}
		{#if replay.error}
			<p class="text-destructive pt-2 text-xs">{replay.error}</p>
		{/if}

		<!-- The Replayer draws the recorded page at its recorded size inside `root`; the scale makes it fit this box. White, like a browser's
		     default canvas, because most pages set no background of their own and are dark text on it. -->
		<div bind:this={measure} class="overflow-hidden rounded-md bg-white {replay.ready ? '' : 'hidden'}" style:height="{replay.viewportHeight * replay.scale}px">
			<div
				bind:this={root}
				class="origin-top-left"
				style:width="{replay.viewportWidth}px"
				style:transform="scale({replay.scale})"
			></div>
		</div>

		{#if replay.ready}
			<div class="mt-2 flex flex-wrap items-center gap-2">
				<Button
					variant="outline"
					size="icon-sm"
					aria-label={replay.playing ? m.sessionsPage_webReplay_pause() : m.sessionsPage_webReplay_play()}
					onclick={() => replay.toggle()}
				>
					{#if replay.playing}<PauseIcon />{:else}<PlayIcon />{/if}
				</Button>
				<input
					class="min-w-32 flex-1"
					type="range"
					min="0"
					max={replay.totalMs}
					step="100"
					value={replay.elapsedMs}
					aria-label={m.sessionsPage_webReplay_position()}
					oninput={(e) => replay.seek(Number(e.currentTarget.value))}
				/>
				<div class="flex items-center gap-1" role="group" aria-label={m.sessionsPage_webReplay_speed()}>
					{#each REPLAY_SPEEDS as speed (speed)}
						<Button variant={replay.speed === speed ? 'secondary' : 'ghost'} size="xs" onclick={() => replay.setSpeed(speed)}>{speed}x</Button>
					{/each}
				</div>
				<label class="flex items-center gap-1.5 text-xs">
					<Switch checked={replay.skipInactive} onCheckedChange={(v) => replay.setSkipInactive(v)} size="sm" />
					{m.sessionsPage_webReplay_skipInactive()}
				</label>
			</div>
		{/if}
		{#if replay.ready && (replay.consoleEntries.length > 0 || replay.networkEntries.length > 0)}
			<WebReplayCapture {replay} />
		{/if}
		{#if replay.replay?.truncated}
			<p class="text-muted-foreground pt-2 text-xs">{m.sessionsPage_webReplay_truncated()}</p>
		{/if}
	</section>
{/if}
