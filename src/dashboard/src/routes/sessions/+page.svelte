<script lang="ts">
	// Client-app sessions, grouped from spans' `session.id` attribute (Flare.Maui stamps one per
	// process start, ADR-0166) - see docs-internal/adr/0167-app-sessions-view.md.
	import { onMount, onDestroy } from 'svelte';
	import * as Select from '$lib/components/ui/select';
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import * as Switch from '$lib/components/ui/switch';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import SmartphoneIcon from '@lucide/svelte/icons/smartphone';
	import { AppSessionsState, APP_SESSIONS_WINDOW_PRESETS, type AppSessionsWindowPreset } from '$lib/app-sessions/state.svelte';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import { crashFreePercent } from '$lib/app-sessions-api';
	import { buildSessionTimelineHref } from '$lib/deep-links';
	import { formatCount } from '$lib/ingestion/format';
	import { formatDateTime } from '$lib/time/format';
	import { formatAgo } from '$lib/components/metric-catalog/format';
	import * as m from '$lib/paraglide/messages';

	const sessions = new AppSessionsState();

	// Sentinel for "all" - bits-ui's Select can't carry an empty-string item value.
	const ALL = '__all__';

	onMount(() => sessions.load());
	onDestroy(() => sessions.dispose());

	// A session's last span can be seconds old; the timeline link spans first-to-last with a minute either side.
	const PAD_MS = 60_000;

	function formatRate(crashed: number, total: number): string {
		const pct = crashFreePercent(crashed, total);
		return pct === null ? '—' : `${pct.toFixed(pct >= 99.995 || pct === 0 ? 0 : 2)}%`;
	}

	// Red below 99%, the usual release-health bar for a mobile app.
	function rateClass(crashed: number, total: number): string {
		const pct = crashFreePercent(crashed, total);
		return pct !== null && pct < 99 ? 'text-destructive font-medium' : '';
	}
</script>

<svelte:head>
	<title>{m.sessionsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
		<h1 class="text-sm font-medium">{m.sessionsPage_heading()}</h1>
		<Select.Root type="single" value={sessions.service || ALL} onValueChange={(v) => sessions.setService(v === ALL ? '' : v)}>
			<Select.Trigger class="ml-2 w-auto" aria-label={m.sessionsPage_serviceFilterLabel()}>
				{sessions.service || m.sessionsPage_allServices()}
			</Select.Trigger>
			<Select.Content>
				<Select.Item value={ALL} label={m.sessionsPage_allServices()} />
				{#each sessions.services as service (service)}
					<Select.Item value={service} label={service} />
				{/each}
			</Select.Content>
		</Select.Root>
		<Select.Root type="single" value={sessions.version || ALL} onValueChange={(v) => sessions.setVersion(v === ALL ? '' : v)}>
			<Select.Trigger class="w-auto" aria-label={m.sessionsPage_versionFilterLabel()}>
				{sessions.version || m.sessionsPage_allVersions()}
			</Select.Trigger>
			<Select.Content>
				<Select.Item value={ALL} label={m.sessionsPage_allVersions()} />
				{#each sessions.versions as version (version)}
					<Select.Item value={version} label={version} />
				{/each}
			</Select.Content>
		</Select.Root>
		<label class="flex items-center gap-2 text-xs">
			<Switch.Root checked={sessions.errorsOnly} onCheckedChange={(v) => sessions.setErrorsOnly(v)} />
			{m.sessionsPage_errorsOnly()}
		</label>
		<Select.Root type="single" value={sessions.windowPreset} onValueChange={(v) => v && sessions.setWindowPreset(v as AppSessionsWindowPreset)}>
			<Select.Trigger class="ml-auto w-auto">
				<ClockIcon data-icon="inline-start" />
				{servicesWindowPresetLabel(sessions.windowPreset)}
			</Select.Trigger>
			<Select.Content>
				{#each APP_SESSIONS_WINDOW_PRESETS as preset (preset.value)}
					<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
				{/each}
			</Select.Content>
		</Select.Root>
		<Button variant="outline" size="sm" onclick={() => sessions.load()} disabled={sessions.loading}>
			{#if sessions.loading}
				<Spinner class="size-4" data-icon="inline-start" />
			{:else}
				<RefreshCwIcon data-icon="inline-start" />
			{/if}
			{m.sessionsPage_refresh()}
		</Button>
	</div>

	<div class="px-4 pb-4">
		{#if sessions.error}
			<p class="text-destructive py-2 text-xs">{sessions.error}</p>
		{/if}
		{#if sessions.loading && !sessions.sessions}
			<div class="flex h-32 items-center justify-center">
				<Spinner />
			</div>
		{:else if !sessions.sessions || sessions.sessions.length === 0}
			<Empty.Root>
				<Empty.Header>
					<Empty.Media variant="icon"><SmartphoneIcon /></Empty.Media>
					<Empty.Title>{m.sessionsPage_emptyTitle()}</Empty.Title>
					<Empty.Description>{m.sessionsPage_emptyDescription()}</Empty.Description>
				</Empty.Header>
			</Empty.Root>
		{:else}
			{#if sessions.releaseHealth && sessions.releaseHealth.length > 0}
				<h2 class="pt-4 pb-1 text-xs font-medium">{m.sessionsPage_health_heading()}</h2>
				<Table.Root>
					<Table.Header>
						<Table.Row>
							<Table.Head>{m.sessionsPage_versionColumn()}</Table.Head>
							<Table.Head class="text-right">{m.sessionsPage_health_sessions()}</Table.Head>
							<Table.Head class="text-right">{m.sessionsPage_health_crashFreeSessions()}</Table.Head>
							<Table.Head class="text-right">{m.sessionsPage_health_users()}</Table.Head>
							<Table.Head class="text-right">{m.sessionsPage_health_crashFreeUsers()}</Table.Head>
						</Table.Row>
					</Table.Header>
					<Table.Body>
						{#each sessions.releaseHealth as row (row.version)}
							<Table.Row>
								<Table.Cell class="font-medium">{row.version || '—'}</Table.Cell>
								<Table.Cell class="text-right tabular-nums">{formatCount(row.sessions)}</Table.Cell>
								<Table.Cell
									class="text-right tabular-nums {rateClass(row.crashedSessions, row.sessions)}"
									title={m.sessionsPage_health_crashedOf({ crashed: row.crashedSessions, total: row.sessions })}
								>
									{formatRate(row.crashedSessions, row.sessions)}
								</Table.Cell>
								<Table.Cell class="text-muted-foreground text-right tabular-nums">{row.users > 0 ? formatCount(row.users) : '—'}</Table.Cell>
								<Table.Cell class="text-right tabular-nums {rateClass(row.crashedUsers, row.users)}">
									{formatRate(row.crashedUsers, row.users)}
								</Table.Cell>
							</Table.Row>
						{/each}
					</Table.Body>
				</Table.Root>
				<p class="text-muted-foreground pt-1 pb-4 text-xs">{m.sessionsPage_health_hint()}</p>
			{/if}
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head>{m.sessionsPage_sessionColumn()}</Table.Head>
						<Table.Head>{m.sessionsPage_serviceColumn()}</Table.Head>
						<Table.Head>{m.sessionsPage_versionColumn()}</Table.Head>
						<Table.Head>{m.sessionsPage_deviceColumn()}</Table.Head>
						<Table.Head>{m.sessionsPage_screensColumn()}</Table.Head>
						<Table.Head class="text-right">{m.sessionsPage_tracesColumn()}</Table.Head>
						<Table.Head class="text-right">{m.sessionsPage_errorsColumn()}</Table.Head>
						<Table.Head class="text-right">{m.sessionsPage_lastSeenColumn()}</Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each sessions.sessions as row (row.sessionId)}
						<Table.Row>
							<Table.Cell class="font-mono text-xs">
								<a
									class="hover:underline"
									href={buildSessionTimelineHref(row.sessionId, row.firstSeenUnixMs - PAD_MS, row.lastSeenUnixMs + PAD_MS)}
									title={m.sessionsPage_viewTimeline()}
								>
									{row.sessionId.slice(0, 8)}
								</a>
							</Table.Cell>
							<Table.Cell class="font-medium">{row.serviceName}</Table.Cell>
							<Table.Cell class="text-muted-foreground">{row.version || '—'}</Table.Cell>
							<Table.Cell class="text-muted-foreground" title={row.os}>{row.device || row.os || '—'}</Table.Cell>
							<Table.Cell class="text-muted-foreground max-w-64 truncate" title={row.screens.join(', ')}>
								{row.screens.join(' › ') || '—'}
							</Table.Cell>
							<Table.Cell class="text-right tabular-nums">{formatCount(row.traceCount)}</Table.Cell>
							<Table.Cell class="text-right tabular-nums {row.errorCount > 0 ? 'text-destructive font-medium' : ''}">
								{formatCount(row.errorCount)}
							</Table.Cell>
							<Table.Cell class="text-muted-foreground text-right tabular-nums" title={formatDateTime(row.lastSeenUnixMs)}>
								{formatAgo(row.lastSeenUnixMs, sessions.windowEndMs)}
							</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
			{#if sessions.truncated}
				<p class="text-muted-foreground pt-3 text-xs">{m.sessionsPage_truncated()}</p>
			{/if}
			<p class="text-muted-foreground pt-3 text-xs">{m.sessionsPage_hint()}</p>
		{/if}
	</div>
</div>
