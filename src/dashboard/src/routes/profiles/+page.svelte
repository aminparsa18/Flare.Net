<script lang="ts">
	// Continuous profiling: the merged call tree of one service's samples of one type
	// (cpu, alloc_space, ...) over a window, optionally narrowed to one span via
	// `?traceId=&spanId=` (the "View profile" link in SpanDetailSheet). ADR-0141.
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { withBase } from '$lib/paths';
	import { getFlameGraph, getProfileTypes, type FlameGraphResponse, type ProfileTypeInfo } from '$lib/profiles-api';
	import { SERVICES_WINDOW_PRESETS, servicesWindowPresetLabel, type ServicesWindowPreset } from '$lib/services/state.svelte';
	import ProfileFlameGraph from '$lib/components/profiles/ProfileFlameGraph.svelte';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import XIcon from '@lucide/svelte/icons/x';
	import * as m from '$lib/paraglide/messages';

	const params = page.url.searchParams;
	let windowPreset = $state<ServicesWindowPreset>((params.get('window') as ServicesWindowPreset) || '1h');
	let service = $state(params.get('service') ?? '');
	let sampleType = $state(params.get('sampleType') ?? '');
	let traceId = $state(params.get('traceId') ?? '');
	let spanId = $state(params.get('spanId') ?? '');

	let types = $state<ProfileTypeInfo[]>([]);
	let graph = $state<FlameGraphResponse | null>(null);
	let loading = $state(false);
	let error = $state<string | null>(null);
	let typesLoaded = $state(false);
	let controller: AbortController | undefined;

	const services = $derived([...new Set(types.map((t) => t.service))].sort());
	const sampleTypes = $derived(types.filter((t) => t.service === service));
	const windowMinutes = $derived(SERVICES_WINDOW_PRESETS.find((p) => p.value === windowPreset)?.minutes ?? 60);

	function syncUrl() {
		const q = new URLSearchParams();
		if (service) q.set('service', service);
		if (sampleType) q.set('sampleType', sampleType);
		if (traceId) q.set('traceId', traceId);
		if (spanId) q.set('spanId', spanId);
		if (windowPreset !== '1h') q.set('window', windowPreset);
		const qs = q.toString();
		void goto(withBase(`/profiles${qs ? `?${qs}` : ''}`), { replaceState: true, keepFocus: true, noScroll: true });
	}

	async function load() {
		controller?.abort();
		const mine = (controller = new AbortController());
		loading = true;
		error = null;
		try {
			const typesResponse = await getProfileTypes(windowMinutes, mine.signal);
			types = typesResponse.types;
			typesLoaded = true;
			// Default to the first series when nothing (valid) is selected.
			if (!types.some((t) => t.service === service)) service = types[0]?.service ?? '';
			if (!types.some((t) => t.service === service && t.sampleType === sampleType)) {
				sampleType = types.find((t) => t.service === service)?.sampleType ?? '';
			}
			if (service && sampleType) {
				graph = await getFlameGraph(
					{ service, sampleType, windowMinutes, traceId: traceId || undefined, spanId: spanId || undefined },
					mine.signal
				);
			} else {
				graph = null;
			}
			syncUrl();
		} catch (e) {
			if (mine.signal.aborted) return;
			error = e instanceof Error ? e.message : String(e);
		} finally {
			if (controller === mine) loading = false;
		}
	}

	function clearSpanFilter() {
		traceId = '';
		spanId = '';
		void load();
	}

	onMount(() => {
		void load();
		return () => controller?.abort();
	});
</script>

<svelte:head>
	<title>{m.profilesPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<div class="bg-background flex flex-wrap items-center gap-2 border-b px-4 py-2">
		<Select.Root
			type="single"
			value={service}
			onValueChange={(v) => {
				if (!v) return;
				service = v;
				sampleType = '';
				void load();
			}}
		>
			<Select.Trigger class="w-48">{service || m.profilesPage_service()}</Select.Trigger>
			<Select.Content>
				{#each services as s (s)}
					<Select.Item value={s} label={s} />
				{/each}
			</Select.Content>
		</Select.Root>
		<Select.Root
			type="single"
			value={sampleType}
			onValueChange={(v) => {
				if (!v) return;
				sampleType = v;
				void load();
			}}
		>
			<Select.Trigger class="w-44">{sampleType || m.profilesPage_sampleType()}</Select.Trigger>
			<Select.Content>
				{#each sampleTypes as t (t.sampleType)}
					<Select.Item value={t.sampleType} label={t.sampleType} />
				{/each}
			</Select.Content>
		</Select.Root>
		<Select.Root
			type="single"
			value={windowPreset}
			onValueChange={(v) => {
				if (!v) return;
				windowPreset = v as ServicesWindowPreset;
				void load();
			}}
		>
			<Select.Trigger class="ml-auto w-auto">
				<ClockIcon data-icon="inline-start" />
				{servicesWindowPresetLabel(windowPreset)}
			</Select.Trigger>
			<Select.Content>
				{#each SERVICES_WINDOW_PRESETS as preset (preset.value)}
					<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
				{/each}
			</Select.Content>
		</Select.Root>
		<Button size="sm" onclick={load} disabled={loading}>
			{#if loading}
				<Spinner class="size-4" data-icon="inline-start" />
			{:else}
				<RefreshCwIcon data-icon="inline-start" />
			{/if}
			{m.profilesPage_refresh()}
		</Button>
	</div>

	{#if spanId || traceId}
		<div class="flex items-center gap-2 border-b px-4 py-1.5 text-xs">
			<span class="text-muted-foreground">{m.profilesPage_spanFilter({ id: spanId || traceId })}</span>
			{#if traceId}
				<a class="text-primary hover:underline" href={withBase(`/traces/${traceId}${spanId ? `?span=${spanId}` : ''}`)}>{m.profilesPage_viewTrace()}</a>
			{/if}
			<Button variant="ghost" size="xs" onclick={clearSpanFilter}>
				<XIcon data-icon="inline-start" />
				{m.profilesPage_clearSpan()}
			</Button>
		</div>
	{/if}

	{#if error}
		<p class="text-destructive px-4 py-3 text-sm">{error}</p>
	{:else if typesLoaded && types.length === 0}
		<p class="text-muted-foreground px-4 py-6 text-sm">{m.profilesPage_noTypes()}</p>
	{:else if graph && graph.root.total === 0}
		<p class="text-muted-foreground px-4 py-6 text-sm">{m.profilesPage_empty()}</p>
	{:else if graph}
		<ProfileFlameGraph {graph} />
	{:else if !typesLoaded}
		<p class="text-muted-foreground px-4 py-3 text-sm">{m.profilesPage_intro()}</p>
	{/if}
</div>
