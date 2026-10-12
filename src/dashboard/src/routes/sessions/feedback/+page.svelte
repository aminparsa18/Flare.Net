<script lang="ts">
	// Inbox of messages users sent from the app ("report a problem", ADR-0193). Each row links to its session timeline.
	import { onMount, onDestroy } from 'svelte';
	import * as Select from '$lib/components/ui/select';
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import MessageSquareIcon from '@lucide/svelte/icons/message-square';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import { AppFeedbackState, APP_FEEDBACK_WINDOW_PRESETS, FEEDBACK_SESSION_LOOKBACK_MS } from '$lib/app-sessions/feedback.svelte';
	import { servicesWindowPresetLabel, type ServicesWindowPreset } from '$lib/services/state.svelte';
	import { buildSessionTimelineHref } from '$lib/deep-links';
	import { withBase } from '$lib/paths';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const feedback = new AppFeedbackState();

	// Sentinel for "all" - bits-ui's Select can't carry an empty-string item value.
	const ALL = '__all__';

	onMount(() => feedback.load());
	onDestroy(() => feedback.dispose());

	function sessionHref(sessionId: string, timeUnixMs: number): string {
		return buildSessionTimelineHref(sessionId, timeUnixMs - FEEDBACK_SESSION_LOOKBACK_MS, timeUnixMs + 60_000);
	}
</script>

<svelte:head>
	<title>{m.feedbackPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
		<Button variant="ghost" size="sm" href={withBase('/sessions')}>
			<ArrowLeftIcon data-icon="inline-start" />
			{m.feedbackPage_back()}
		</Button>
		<h1 class="text-sm font-medium">{m.feedbackPage_heading()}</h1>
		<Select.Root type="single" value={feedback.service || ALL} onValueChange={(v) => feedback.setService(v === ALL ? '' : v)}>
			<Select.Trigger class="ml-2 w-auto" aria-label={m.sessionsPage_serviceFilterLabel()}>
				{feedback.service || m.sessionsPage_allServices()}
			</Select.Trigger>
			<Select.Content>
				<Select.Item value={ALL} label={m.sessionsPage_allServices()} />
				{#each feedback.services as service (service)}
					<Select.Item value={service} label={service} />
				{/each}
			</Select.Content>
		</Select.Root>
		<Select.Root type="single" value={feedback.windowPreset} onValueChange={(v) => v && feedback.setWindowPreset(v as ServicesWindowPreset)}>
			<Select.Trigger class="ml-auto w-auto">
				<ClockIcon data-icon="inline-start" />
				{servicesWindowPresetLabel(feedback.windowPreset)}
			</Select.Trigger>
			<Select.Content>
				{#each APP_FEEDBACK_WINDOW_PRESETS as preset (preset.value)}
					<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
				{/each}
			</Select.Content>
		</Select.Root>
		<Button variant="outline" size="sm" onclick={() => feedback.load()} disabled={feedback.loading}>
			{#if feedback.loading}
				<Spinner class="size-4" data-icon="inline-start" />
			{:else}
				<RefreshCwIcon data-icon="inline-start" />
			{/if}
			{m.sessionsPage_refresh()}
		</Button>
	</div>

	<div class="px-4 pb-4">
		{#if feedback.error}
			<p class="text-destructive py-2 text-xs">{feedback.error}</p>
		{/if}
		{#if feedback.loading && !feedback.items}
			<div class="flex h-32 items-center justify-center">
				<Spinner />
			</div>
		{:else if !feedback.items || feedback.items.length === 0}
			<Empty.Root>
				<Empty.Header>
					<Empty.Media variant="icon"><MessageSquareIcon /></Empty.Media>
					<Empty.Title>{m.feedbackPage_emptyTitle()}</Empty.Title>
					<Empty.Description>{m.feedbackPage_emptyDescription()}</Empty.Description>
				</Empty.Header>
			</Empty.Root>
		{:else}
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.feedbackPage_timeColumn()}</Table.Head>
						<Table.Head>{m.feedbackPage_userColumn()}</Table.Head>
						<Table.Head>{m.sessionsPage_serviceColumn()}</Table.Head>
						<Table.Head>{m.feedbackPage_messageColumn()}</Table.Head>
						<Table.Head></Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each feedback.items as item (item.feedbackId)}
						<Table.Row>
							<Table.Cell class="text-muted-foreground align-top text-xs whitespace-nowrap">{formatDateTime(item.timeUnixMs)}</Table.Cell>
							<Table.Cell class="align-top text-xs">
								{#if item.name || item.email || item.userId}
									{#if item.name}<div class="font-medium">{item.name}</div>{/if}
									{#if item.email}<a class="text-primary hover:underline" href={`mailto:${item.email}`}>{item.email}</a>{/if}
									{#if item.userId}<div class="text-muted-foreground font-mono">{item.userId}</div>{/if}
								{:else}
									<span class="text-muted-foreground">{m.feedbackPage_anonymous()}</span>
								{/if}
							</Table.Cell>
							<Table.Cell class="align-top text-xs">
								{item.serviceName}
								{#if item.appVersion}<span class="text-muted-foreground"> {item.appVersion}</span>{/if}
							</Table.Cell>
							<Table.Cell class="max-w-xl align-top text-sm break-words whitespace-pre-wrap">{item.message}</Table.Cell>
							<Table.Cell class="align-top">
								<Button variant="outline" size="sm" href={sessionHref(item.sessionId, item.timeUnixMs)}>{m.feedbackPage_openSession()}</Button>
							</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
			{#if feedback.truncated}
				<p class="text-muted-foreground pt-2 text-xs">{m.feedbackPage_truncated()}</p>
			{/if}
		{/if}
	</div>
</div>
