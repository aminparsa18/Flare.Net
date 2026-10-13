<script lang="ts">
	// Session replay-lite (ADR-0192): the scheduled screenshots of one session behind a scrubber, with the span the
	// app was in at that moment. Renders nothing when the session has no frames.
	import { Button } from '#lib/components/ui/button/index.ts';
	import { Badge } from '#lib/components/ui/badge/index.ts';
	import { Spinner } from '#lib/components/ui/spinner/index.ts';
	import PlayIcon from '@lucide/svelte/icons/play';
	import PauseIcon from '@lucide/svelte/icons/pause';
	import SkipBackIcon from '@lucide/svelte/icons/skip-back';
	import SkipForwardIcon from '@lucide/svelte/icons/skip-forward';
	import type { AppSessionReplayState } from '#lib/app-sessions/replay.svelte.ts';
	import { eventAt } from '#lib/app-sessions/replay.svelte.ts';
	import { formatOffset } from '#lib/app-sessions/timeline.svelte.ts';
	import type { AppSessionTimelineEvent } from '#lib/app-sessions-api.ts';
	import { formatDateTime } from '#lib/time/format.ts';
	import * as m from '#lib/paraglide/messages.js';

	let { replay, events, origin }: { replay: AppSessionReplayState; events: AppSessionTimelineEvent[]; origin: number } = $props();

	const total = $derived(replay.times.length);
	const time = $derived(replay.currentTime);
	const nearest = $derived(time === undefined ? undefined : eventAt(events, time));
</script>

{#if total > 0}
	<section class="mb-4 rounded-md border p-3">
		<div class="mb-2 flex flex-wrap items-center gap-2">
			<h2 class="text-sm font-medium">{m.sessionsPage_replay_heading()}</h2>
			<span class="text-muted-foreground text-xs">{m.sessionsPage_replay_frame({ n: replay.index + 1, total })}</span>
			{#if time !== undefined}
				<span class="text-muted-foreground font-mono text-xs tabular-nums" title={formatDateTime(time)}>{formatOffset(time - origin)}</span>
			{/if}
			{#if nearest}
				<Badge variant={nearest.isError ? 'destructive' : 'outline'} title={m.sessionsPage_replay_nearest()}>{nearest.name}</Badge>
			{/if}
		</div>

		<div class="bg-muted flex min-h-48 items-center justify-center rounded-md">
			{#if replay.currentUrl}
				<img class="max-h-[28rem] rounded-md" src={replay.currentUrl} alt={m.sessionsPage_replay_heading()} />
			{:else if replay.currentUrl === null}
				<p class="text-muted-foreground text-xs">{m.sessionsPage_detail_screenshotMissing()}</p>
			{:else}
				<Spinner />
			{/if}
		</div>

		<div class="mt-2 flex items-center gap-2">
			<Button variant="outline" size="icon-sm" aria-label={m.sessionsPage_replay_previous()} onclick={() => replay.select(replay.index - 1)} disabled={replay.index === 0}>
				<SkipBackIcon />
			</Button>
			<Button variant="outline" size="icon-sm" aria-label={replay.playing ? m.sessionsPage_replay_pause() : m.sessionsPage_replay_play()} onclick={() => replay.toggle()}>
				{#if replay.playing}<PauseIcon />{:else}<PlayIcon />{/if}
			</Button>
			<Button variant="outline" size="icon-sm" aria-label={m.sessionsPage_replay_next()} onclick={() => replay.select(replay.index + 1)} disabled={replay.index >= total - 1}>
				<SkipForwardIcon />
			</Button>
			<input
				class="min-w-0 flex-1"
				type="range"
				min="0"
				max={total - 1}
				value={replay.index}
				aria-label={m.sessionsPage_replay_heading()}
				oninput={(e) => replay.select(Number(e.currentTarget.value))}
			/>
		</div>
		{#if replay.replay?.truncated}
			<p class="text-muted-foreground pt-2 text-xs">{m.sessionsPage_replay_truncated()}</p>
		{/if}
	</section>
{/if}
