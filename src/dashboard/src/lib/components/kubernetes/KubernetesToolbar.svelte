<script lang="ts">
	// Tabs are local state with plain buttonVariants styling - same pattern as
	// TracesViewTabs.svelte (this dashboard has no shadcn Tabs component).
	import * as Select from '$lib/components/ui/select';
	import { Input } from '$lib/components/ui/input';
	import { Badge } from '$lib/components/ui/badge';
	import { buttonVariants } from '$lib/components/ui/button';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import XIcon from '@lucide/svelte/icons/x';
	import { kubernetesContext } from '$lib/kubernetes/context';
	import { KUBERNETES_EVENT_TYPES } from '$lib/kubernetes/events';
	import { KUBERNETES_WINDOW_PRESETS, type KubernetesTab, type KubernetesWindowPreset } from '$lib/kubernetes/state.svelte';
	import { KUBERNETES_WORKLOAD_KINDS, type KubernetesWorkloadKind } from '$lib/kubernetes-api';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import { cn } from '$lib/utils';
	import * as m from '$lib/paraglide/messages';

	const k8s = kubernetesContext.get();

	// Sentinel for "all" - bits-ui's Select can't carry an empty-string item value.
	const ALL = '__all__';

	const tabs: { tab: KubernetesTab; label: () => string }[] = [
		{ tab: 'nodes', label: m.kubernetesPage_nodesTab },
		{ tab: 'namespaces', label: m.kubernetesPage_namespacesTab },
		{ tab: 'workloads', label: m.kubernetesPage_workloadsTab },
		{ tab: 'pods', label: m.kubernetesPage_podsTab },
		{ tab: 'volumes', label: m.kubernetesPage_volumesTab },
		{ tab: 'events', label: m.kubernetesPage_eventsTab }
	];

	const searchPlaceholder = $derived(
		{
			nodes: m.kubernetesPage_nodeSearchPlaceholder,
			namespaces: m.kubernetesPage_namespaceSearchPlaceholder,
			workloads: m.kubernetesPage_workloadSearchPlaceholder,
			pods: m.kubernetesPage_podSearchPlaceholder,
			volumes: m.kubernetesPage_volumeSearchPlaceholder,
			events: m.kubernetesPage_eventSearchPlaceholder
		}[k8s.tab]()
	);
</script>

{#snippet filter(value: string, options: string[], allLabel: string, ariaLabel: string, onChange: (v: string) => void)}
	<Select.Root type="single" value={value || ALL} onValueChange={(v) => onChange(v === ALL ? '' : v)}>
		<Select.Trigger class="w-auto max-w-56" aria-label={ariaLabel}>
			<span class="truncate">{value || allLabel}</span>
		</Select.Trigger>
		<Select.Content>
			<Select.Item value={ALL} label={allLabel} />
			{#each options as option (option)}
				<Select.Item value={option} label={option} />
			{/each}
		</Select.Content>
	</Select.Root>
{/snippet}

{#snippet namespaceFilter()}
	{@render filter(k8s.namespace, k8s.knownNamespaces, m.kubernetesPage_allNamespaces(), m.kubernetesPage_namespaceFilterLabel(), (v) =>
		k8s.setNamespace(v)
	)}
{/snippet}

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<h1 class="text-sm font-medium">{m.kubernetesPage_heading()}</h1>
	<div class="flex flex-wrap items-center gap-1">
		{#each tabs as { tab, label } (tab)}
			<button
				type="button"
				class={cn(buttonVariants({ variant: k8s.tab === tab ? 'secondary' : 'ghost', size: 'sm' }))}
				aria-pressed={k8s.tab === tab}
				onclick={() => k8s.setTab(tab)}
			>
				{label()}
			</button>
		{/each}
	</div>
	{#if k8s.tab === 'workloads'}
		<Select.Root type="single" value={k8s.workloadKind} onValueChange={(v) => v && k8s.setWorkloadKind(v as KubernetesWorkloadKind)}>
			<Select.Trigger class="ml-2 w-auto" aria-label={m.kubernetesPage_workloadKindLabel()}>
				{k8s.workloadKind}
			</Select.Trigger>
			<Select.Content>
				{#each KUBERNETES_WORKLOAD_KINDS as kind (kind)}
					<Select.Item value={kind} label={kind} />
				{/each}
			</Select.Content>
		</Select.Root>
	{/if}
	<Input
		type="search"
		class={cn('h-8 w-56', k8s.tab !== 'workloads' && 'ml-2')}
		placeholder={searchPlaceholder}
		aria-label={searchPlaceholder}
		value={k8s.search[k8s.tab]}
		oninput={(e) => k8s.setSearch(e.currentTarget.value)}
	/>
	{#if k8s.tab === 'nodes'}
		{#if k8s.knownClusters.length > 0 || k8s.clusterName}
			{@render filter(k8s.clusterName, k8s.knownClusters, m.kubernetesPage_allClusters(), m.kubernetesPage_clusterFilterLabel(), (v) =>
				k8s.setClusterName(v)
			)}
		{/if}
	{:else if k8s.tab === 'pods'}
		{@render namespaceFilter()}
		{@render filter(k8s.nodeFilter, k8s.knownNodes, m.kubernetesPage_allNodes(), m.kubernetesPage_nodeFilterLabel(), (v) => k8s.setNodeFilter(v))}
		{#if k8s.workloadFilter}
			<Badge variant="secondary" class="gap-1 pr-1">
				<span class="text-muted-foreground">{k8s.workloadFilter.kind}</span>
				{k8s.workloadFilter.name}
				<button
					type="button"
					class="hover:bg-muted rounded-sm p-0.5"
					aria-label={m.kubernetesPage_clearWorkloadFilter()}
					title={m.kubernetesPage_clearWorkloadFilter()}
					onclick={() => k8s.setWorkloadFilter(null)}
				>
					<XIcon class="size-3" />
				</button>
			</Badge>
		{/if}
	{:else if k8s.tab === 'workloads' || k8s.tab === 'volumes'}
		{@render namespaceFilter()}
	{:else if k8s.tab === 'events'}
		{@render namespaceFilter()}
		{@render filter(k8s.eventType, [...KUBERNETES_EVENT_TYPES], m.kubernetesPage_allEventTypes(), m.kubernetesPage_eventTypeFilterLabel(), (v) =>
			k8s.setEventType(v)
		)}
		{#if k8s.eventObject}
			<Badge variant="secondary" class="gap-1 pr-1">
				<span class="text-muted-foreground">{k8s.eventObject.kind}</span>
				{k8s.eventObject.name}
				<button
					type="button"
					class="hover:bg-muted rounded-sm p-0.5"
					aria-label={m.kubernetesPage_clearEventObjectFilter()}
					title={m.kubernetesPage_clearEventObjectFilter()}
					onclick={() => k8s.setEventObject(null)}
				>
					<XIcon class="size-3" />
				</button>
			</Badge>
		{/if}
	{/if}
	<Select.Root type="single" value={k8s.windowPreset} onValueChange={(v) => v && k8s.setWindowPreset(v as KubernetesWindowPreset)}>
		<Select.Trigger class="ml-auto w-auto">
			<ClockIcon data-icon="inline-start" />
			{servicesWindowPresetLabel(k8s.windowPreset)}
		</Select.Trigger>
		<Select.Content>
			{#each KUBERNETES_WINDOW_PRESETS as preset (preset.value)}
				<Select.Item value={preset.value} label={servicesWindowPresetLabel(preset.value)} />
			{/each}
		</Select.Content>
	</Select.Root>
</div>
