<script lang="ts">
	// Tabs are local state with plain buttonVariants styling - same pattern as
	// TracesViewTabs.svelte (this dashboard has no shadcn Tabs component).
	import * as Select from '$lib/components/ui/select';
	import { Input } from '$lib/components/ui/input';
	import { buttonVariants } from '$lib/components/ui/button';
	import ClockIcon from '@lucide/svelte/icons/clock';
	import { kubernetesContext } from '$lib/kubernetes/context';
	import { KUBERNETES_WINDOW_PRESETS, type KubernetesTab, type KubernetesWindowPreset } from '$lib/kubernetes/state.svelte';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import { cn } from '$lib/utils';
	import * as m from '$lib/paraglide/messages';

	const k8s = kubernetesContext.get();

	// Sentinel for "all" - bits-ui's Select can't carry an empty-string item value.
	const ALL = '__all__';

	const tabs: { tab: KubernetesTab; label: () => string }[] = [
		{ tab: 'nodes', label: m.kubernetesPage_nodesTab },
		{ tab: 'pods', label: m.kubernetesPage_podsTab }
	];
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

<div class="bg-background sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b px-4 py-2">
	<h1 class="text-sm font-medium">{m.kubernetesPage_heading()}</h1>
	<div class="flex items-center gap-1">
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
	{#if k8s.tab === 'nodes'}
		<Input
			type="search"
			class="ml-2 h-8 w-56"
			placeholder={m.kubernetesPage_nodeSearchPlaceholder()}
			aria-label={m.kubernetesPage_nodeSearchPlaceholder()}
			value={k8s.nodeSearch}
			oninput={(e) => k8s.setSearch(e.currentTarget.value)}
		/>
		{#if k8s.knownClusters.length > 0 || k8s.clusterName}
			{@render filter(k8s.clusterName, k8s.knownClusters, m.kubernetesPage_allClusters(), m.kubernetesPage_clusterFilterLabel(), (v) =>
				k8s.setClusterName(v)
			)}
		{/if}
	{:else}
		<Input
			type="search"
			class="ml-2 h-8 w-56"
			placeholder={m.kubernetesPage_podSearchPlaceholder()}
			aria-label={m.kubernetesPage_podSearchPlaceholder()}
			value={k8s.podSearch}
			oninput={(e) => k8s.setSearch(e.currentTarget.value)}
		/>
		{@render filter(k8s.namespace, k8s.knownNamespaces, m.kubernetesPage_allNamespaces(), m.kubernetesPage_namespaceFilterLabel(), (v) =>
			k8s.setNamespace(v)
		)}
		{@render filter(k8s.nodeFilter, k8s.knownNodes, m.kubernetesPage_allNodes(), m.kubernetesPage_nodeFilterLabel(), (v) => k8s.setNodeFilter(v))}
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
