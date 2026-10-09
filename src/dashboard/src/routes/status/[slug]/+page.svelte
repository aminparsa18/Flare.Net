<script lang="ts">
	// Public page (see +layout.svelte's PUBLIC_ROUTES): the status page at /status/{slug}, readable without a
	// session while an admin has it enabled (ADR-0158). Shows only display names, states and uptime.
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { getPublicStatus, subscribeToStatus, type PublicStatusPage, type StatusIncidentStatus, type StatusState } from '$lib/status-pages-api';
	import { formatDateTime } from '$lib/time/format';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Input } from '$lib/components/ui/input';
	import { Button } from '$lib/components/ui/button';
	import * as m from '$lib/paraglide/messages';

	const slug = $derived(page.params.slug ?? '');
	const REFRESH_MS = 60_000;

	let status = $state<PublicStatusPage | null>(null);
	let notFound = $state(false);
	let error = $state<string | null>(null);
	let loading = $state(true);

	let email = $state('');
	let subscribing = $state(false);
	let subscribed = $state(false);
	let subscribeError = $state<string | null>(null);

	async function subscribe(event: SubmitEvent): Promise<void> {
		event.preventDefault();
		subscribing = true;
		subscribeError = null;
		try {
			await subscribeToStatus(slug, email.trim());
			subscribed = true;
		} catch (err) {
			subscribeError = err instanceof Error ? err.message : String(err);
		} finally {
			subscribing = false;
		}
	}

	async function load(): Promise<void> {
		try {
			const result = await getPublicStatus(slug);
			notFound = result === null;
			status = result;
			error = null;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			loading = false;
		}
	}

	onMount(() => {
		void load();
		const timer = setInterval(() => void load(), REFRESH_MS);
		return () => clearInterval(timer);
	});

	const LABEL: Record<StatusState, () => string> = {
		Operational: () => m.statusPublic_operational(),
		Degraded: () => m.statusPublic_degraded(),
		Outage: () => m.statusPublic_outage(),
		Unknown: () => m.statusPublic_unknown()
	};
	const BANNER: Record<StatusState, () => string> = {
		Operational: () => m.statusPublic_bannerOperational(),
		Degraded: () => m.statusPublic_bannerDegraded(),
		Outage: () => m.statusPublic_bannerOutage(),
		Unknown: () => m.statusPublic_bannerUnknown()
	};
	const INCIDENT_LABEL: Record<StatusIncidentStatus, () => string> = {
		Investigating: () => m.statusIncidents_investigating(),
		Identified: () => m.statusIncidents_identified(),
		Monitoring: () => m.statusIncidents_monitoring(),
		Resolved: () => m.statusIncidents_resolved()
	};
	const TONE: Record<StatusState, string> = {
		Operational: 'bg-emerald-500',
		Degraded: 'bg-amber-500',
		Outage: 'bg-red-500',
		Unknown: 'bg-muted-foreground/40'
	};

	function dayTone(percent: number | null): string {
		if (percent === null) return 'bg-muted-foreground/20';
		if (percent >= 99.9) return 'bg-emerald-500';
		if (percent >= 95) return 'bg-amber-500';
		return 'bg-red-500';
	}
</script>

<svelte:head>
	<title>{status?.title ?? m.statusPublic_title()}</title>
	<meta name="robots" content="noindex" />
</svelte:head>

<div class="mx-auto flex min-h-screen w-full max-w-3xl flex-col gap-6 px-4 py-10">
	{#if loading}
		<div class="flex flex-1 items-center justify-center"><Spinner /></div>
	{:else if notFound}
		<p class="text-muted-foreground m-auto text-sm">{m.statusPublic_notFound()}</p>
	{:else if error && !status}
		<p class="text-destructive m-auto text-sm">{error}</p>
	{:else if status}
		<header class="flex flex-col gap-1">
			<h1 class="text-2xl font-semibold">{status.title}</h1>
			{#if status.description}
				<p class="text-muted-foreground text-sm">{status.description}</p>
			{/if}
		</header>

		<div class="flex items-center gap-3 rounded-lg border p-4">
			<span class="size-3 shrink-0 rounded-full {TONE[status.overall]}"></span>
			<span class="font-medium">{BANNER[status.overall]()}</span>
		</div>

		{#if status.incidents.length > 0}
			<section class="flex flex-col gap-3">
				<h2 class="text-sm font-semibold">{m.statusPublic_incidentsHeading()}</h2>
				{#each status.incidents as incident (incident.startedAt + incident.title)}
					<article class="flex flex-col gap-2 rounded-lg border p-4">
						<div class="flex items-center justify-between gap-3">
							<span class="font-medium">{incident.title}</span>
							<span class="text-xs {incident.resolvedAt ? 'text-muted-foreground' : 'text-amber-600 dark:text-amber-400'}">
								{INCIDENT_LABEL[incident.status]()}
							</span>
						</div>
						{#if incident.components.length > 0}
							<span class="text-muted-foreground text-xs">{m.statusPublic_incidentAffects({ components: incident.components.join(', ') })}</span>
						{/if}
						{#if incident.resolvedAt}
							<span class="text-muted-foreground text-xs">{m.statusPublic_incidentResolvedAt({ time: formatDateTime(incident.resolvedAt) })}</span>
						{/if}
						<ol class="flex flex-col gap-2 text-sm">
							{#each incident.updates as update (update.at)}
								<li>
									<span class="font-medium">{INCIDENT_LABEL[update.status]()}</span>
									<span class="text-muted-foreground text-xs"> - {formatDateTime(update.at)}</span>
									<p class="whitespace-pre-wrap">{update.message}</p>
								</li>
							{/each}
						</ol>
					</article>
				{/each}
			</section>
		{/if}

		<ul class="flex flex-col gap-3">
			{#each status.components as component (component.name)}
				<li class="flex flex-col gap-2 rounded-lg border p-4">
					<div class="flex items-center justify-between gap-3">
						<span class="font-medium">{component.name}</span>
						<span class="flex items-center gap-2 text-sm">
							<span class="size-2 rounded-full {TONE[component.state]}"></span>
							{LABEL[component.state]()}
						</span>
					</div>
					<div class="flex h-8 items-stretch gap-px" role="img" aria-label={m.statusPublic_historyLabel({ days: component.days.length })}>
						{#each component.days as day (day.date)}
							<span
								class="min-w-0 flex-1 rounded-[1px] {dayTone(day.uptimePercent)}"
								title={day.uptimePercent === null ? `${day.date}: ${m.statusPublic_noData()}` : `${day.date}: ${day.uptimePercent}%`}
							></span>
						{/each}
					</div>
					<div class="text-muted-foreground flex justify-between text-xs">
						<span>{m.statusPublic_daysAgo({ days: component.days.length })}</span>
						<span>
							{component.uptimePercent === null ? m.statusPublic_noData() : m.statusPublic_uptime({ percent: component.uptimePercent })}
						</span>
						<span>{m.statusPublic_today()}</span>
					</div>
				</li>
			{/each}
		</ul>

		{#if status.subscribable}
			<section class="flex flex-col gap-2 rounded-lg border p-4">
				<h2 class="text-sm font-medium">{m.statusSubscribe_heading()}</h2>
				{#if subscribed}
					<p class="text-sm">{m.statusSubscribe_sent()}</p>
				{:else}
					<form class="flex gap-2" onsubmit={subscribe}>
						<Input type="email" bind:value={email} required maxlength={254} placeholder="you@example.com" aria-label={m.statusSubscribe_emailLabel()} class="flex-1" />
						<Button type="submit" size="sm" disabled={subscribing || email.trim() === ''}>
							{#if subscribing}<Spinner class="size-3.5" />{/if}
							{m.statusSubscribe_button()}
						</Button>
					</form>
					{#if subscribeError}<p class="text-destructive text-xs">{subscribeError}</p>{/if}
					<p class="text-muted-foreground text-xs">{m.statusSubscribe_hint()}</p>
				{/if}
			</section>
		{/if}

		<footer class="text-muted-foreground text-xs">{m.statusPublic_updated({ time: formatDateTime(status.generatedAt) })}</footer>
	{/if}
</div>
