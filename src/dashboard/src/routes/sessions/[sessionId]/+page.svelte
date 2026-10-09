<script lang="ts">
	// One client-app session's spans and errors in time order - see docs-internal/adr/0170-app-session-timeline.md.
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import SmartphoneIcon from '@lucide/svelte/icons/smartphone';
	import { AppSessionTimelineState, formatOffset, barGeometry } from '$lib/app-sessions/timeline.svelte';
	import { buildSessionTracesHref } from '$lib/deep-links';
	import { withBase } from '$lib/paths';
	import { formatDateTime } from '$lib/time/format';
	import { formatDurationNano } from '$lib/traces/duration';
	import * as m from '$lib/paraglide/messages';

	const state = new AppSessionTimelineState();

	// page.params.sessionId is fixed for this component's lifetime - SvelteKit remounts per route param.
	const sessionId = page.params.sessionId!;
	const num = (key: string) => {
		const raw = page.url.searchParams.get(key);
		const n = raw ? Number(raw) : NaN;
		return Number.isFinite(n) && n > 0 ? n : undefined;
	};
	const from = num('from');
	const to = num('to');

	onMount(() => state.load(sessionId, from, to));
	onDestroy(() => state.dispose());

	const events = $derived(state.timeline?.events ?? []);
	const origin = $derived(events[0]?.startUnixMs ?? 0);
	const total = $derived(events.reduce((max, e) => Math.max(max, e.startUnixMs + e.durationMs - origin), 0));
	const errorCount = $derived(events.filter((e) => e.isError).length);
	const tracesHref = $derived(
		buildSessionTracesHref(sessionId, (from ?? origin) - 60_000, (to ?? origin + total) + 60_000)
	);
</script>

<svelte:head>
	<title>{m.sessionsPage_detail_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
		<Button variant="ghost" size="sm" href={withBase('/sessions')}>
			<ArrowLeftIcon data-icon="inline-start" />
			{m.sessionsPage_detail_back()}
		</Button>
		<h1 class="font-mono text-sm font-medium">{m.sessionsPage_detail_heading({ id: sessionId.slice(0, 8) })}</h1>
		{#if state.timeline?.serviceName}
			<span class="text-muted-foreground text-xs">
				{[state.timeline.serviceName, state.timeline.version, state.timeline.device || state.timeline.os].filter(Boolean).join(' · ')}
			</span>
		{/if}
		<Button class="ml-auto" variant="outline" size="sm" href={tracesHref}>{m.sessionsPage_detail_viewTraces()}</Button>
	</div>

	<div class="px-4 pb-4">
		{#if state.error}
			<p class="text-destructive py-2 text-xs">{state.error}</p>
		{/if}
		{#if state.loading && !state.timeline}
			<div class="flex h-32 items-center justify-center"><Spinner /></div>
		{:else if state.timeline && events.length === 0}
			<Empty.Root>
				<Empty.Header>
					<Empty.Media variant="icon"><SmartphoneIcon /></Empty.Media>
					<Empty.Title>{m.sessionsPage_emptyTitle()}</Empty.Title>
					<Empty.Description>{m.sessionsPage_detail_empty()}</Empty.Description>
				</Empty.Header>
			</Empty.Root>
		{:else if state.timeline}
			<dl class="text-muted-foreground flex flex-wrap gap-x-6 gap-y-1 py-3 text-xs">
				<div class="flex gap-1"><dt>{m.sessionsPage_detail_started()}:</dt><dd>{formatDateTime(origin)}</dd></div>
				<div class="flex gap-1"><dt>{m.sessionsPage_detail_spans()}:</dt><dd class="tabular-nums">{events.length}</dd></div>
				<div class="flex gap-1">
					<dt>{m.sessionsPage_detail_errors()}:</dt>
					<dd class="tabular-nums {errorCount > 0 ? 'text-destructive font-medium' : ''}">
						{errorCount > 0 ? errorCount : m.sessionsPage_detail_noErrors()}
					</dd>
				</div>
				<div class="flex gap-1"><dt>{m.sessionsPage_detail_duration()}:</dt><dd class="tabular-nums">{formatDurationNano(total * 1_000_000)}</dd></div>
			</dl>

			<ol class="divide-y rounded-md border">
				{#each events as e (e.traceId + e.spanId)}
					{@const geo = barGeometry(e.startUnixMs, e.durationMs, origin, total)}
					<li class="px-3 py-2 {e.isError ? 'bg-destructive/5' : ''}">
						<div class="flex items-center gap-3">
							<span class="text-muted-foreground w-20 shrink-0 text-right font-mono text-xs tabular-nums" title={formatDateTime(e.startUnixMs)}>
								{formatOffset(e.startUnixMs - origin)}
							</span>
							<a class="min-w-0 flex-1 truncate text-sm hover:underline {e.isError ? 'text-destructive font-medium' : ''}"
								href={withBase(`/traces/${e.traceId}?span=${e.spanId}`)}
								title={m.sessionsPage_detail_openTrace()}>
								{e.name}
							</a>
							{#if e.screen}
								<Badge variant="outline" title={m.sessionsPage_detail_screen()}>{e.screen}</Badge>
							{/if}
							{#if e.isError}
								<Badge variant="destructive">{m.sessionsPage_detail_errorBadge()}</Badge>
							{/if}
							<span class="text-muted-foreground w-16 shrink-0 text-right font-mono text-xs tabular-nums">
								{formatDurationNano(e.durationMs * 1_000_000)}
							</span>
						</div>
						<div class="bg-muted ml-23 mt-1 h-1 rounded-full">
							<div class="h-1 rounded-full {e.isError ? 'bg-destructive' : 'bg-primary'}" style="margin-left: {geo.left}%; width: {geo.width}%"></div>
						</div>
						{#if e.isError && (e.exceptionMessage || e.statusMessage)}
							<p class="text-destructive ml-23 mt-1 break-words text-xs">
								{#if e.exceptionType}<span class="font-mono">{e.exceptionType}: </span>{/if}{e.exceptionMessage || e.statusMessage}
							</p>
						{/if}
					</li>
				{/each}
			</ol>
			{#if state.timeline.truncated}
				<p class="text-muted-foreground pt-3 text-xs">{m.sessionsPage_detail_truncated()}</p>
			{/if}
		{/if}
	</div>
</div>
