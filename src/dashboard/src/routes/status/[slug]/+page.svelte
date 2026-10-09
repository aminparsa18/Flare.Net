<script lang="ts">
	// Public page (see +layout.svelte's PUBLIC_ROUTES): the status page at /status/{slug}, readable without a
	// session while an admin has it enabled (ADR-0158). Shows only display names, states and uptime.
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { getPublicStatus, type PublicStatusPage, type StatusState } from '$lib/status-pages-api';
	import { formatDateTime } from '$lib/time/format';
	import { Spinner } from '$lib/components/ui/spinner';
	import * as m from '$lib/paraglide/messages';

	const slug = $derived(page.params.slug ?? '');
	const REFRESH_MS = 60_000;

	let status = $state<PublicStatusPage | null>(null);
	let notFound = $state(false);
	let error = $state<string | null>(null);
	let loading = $state(true);

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

		<footer class="text-muted-foreground text-xs">{m.statusPublic_updated({ time: formatDateTime(status.generatedAt) })}</footer>
	{/if}
</div>
