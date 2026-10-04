<script lang="ts">
	// Kubernetes Nodes/Pods inventory from ingested OTel kubeletstats/k8s_cluster metrics.
	// Its own route rather than a section of /resources for the same reason /hosts is:
	// /resources shows what Flare discovers by polling (for Kubernetes, only Flare's own
	// pods), while this shows the user's cluster as its collector reports it over OTLP.
	// `?tab=pods` (or namespaces/workloads/volumes/events) opens that tab.
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { KUBERNETES_TABS, KubernetesState, type KubernetesTab } from '$lib/kubernetes/state.svelte';
	import { kubernetesContext } from '$lib/kubernetes/context';
	import KubernetesToolbar from '$lib/components/kubernetes/KubernetesToolbar.svelte';
	import KubernetesNodesTable from '$lib/components/kubernetes/KubernetesNodesTable.svelte';
	import KubernetesNamespacesTable from '$lib/components/kubernetes/KubernetesNamespacesTable.svelte';
	import KubernetesWorkloadsTable from '$lib/components/kubernetes/KubernetesWorkloadsTable.svelte';
	import KubernetesPodsTable from '$lib/components/kubernetes/KubernetesPodsTable.svelte';
	import KubernetesVolumesTable from '$lib/components/kubernetes/KubernetesVolumesTable.svelte';
	import KubernetesEventsTable from '$lib/components/kubernetes/KubernetesEventsTable.svelte';
	import KubernetesDetailSheet from '$lib/components/kubernetes/KubernetesDetailSheet.svelte';
	import * as m from '$lib/paraglide/messages';

	const k8s = kubernetesContext.set(new KubernetesState());

	onMount(() => {
		const tab = page.url.searchParams.get('tab');
		if (KUBERNETES_TABS.includes(tab as KubernetesTab)) k8s.tab = tab as KubernetesTab;
		void k8s.load();
		k8s.startPolling();
	});

	onDestroy(() => {
		k8s.dispose();
	});
</script>

<svelte:head>
	<title>{m.kubernetesPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<KubernetesToolbar />
	{#if k8s.tab === 'nodes'}
		<KubernetesNodesTable />
	{:else if k8s.tab === 'namespaces'}
		<KubernetesNamespacesTable />
	{:else if k8s.tab === 'workloads'}
		<KubernetesWorkloadsTable />
	{:else if k8s.tab === 'pods'}
		<KubernetesPodsTable />
	{:else if k8s.tab === 'events'}
		<KubernetesEventsTable />
	{:else}
		<KubernetesVolumesTable />
	{/if}
	<KubernetesDetailSheet />
</div>
