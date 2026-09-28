<script lang="ts">
	// One node's or one pod's drill-down charts - whichever KubernetesState has selected.
	// Same four-small-charts layout as HostDetailSheet, reusing its HostMetricChart.
	import * as Sheet from '$lib/components/ui/sheet';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import HostMetricChart from '$lib/components/hosts/HostMetricChart.svelte';
	import { kubernetesContext } from '$lib/kubernetes/context';
	import { formatCores, phaseVariant } from '$lib/kubernetes/format';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import * as m from '$lib/paraglide/messages';

	const k8s = kubernetesContext.get();

	interface Chart {
		label: string;
		unit: string | null;
		points: { time: number; value: number | null }[];
	}

	const node = $derived(k8s.selectedNode ? (k8s.nodes?.find((n) => n.nodeName === k8s.selectedNode) ?? null) : null);
	const pod = $derived(
		k8s.selectedPod ? (k8s.pods?.find((p) => p.namespace === k8s.selectedPod!.namespace && p.podName === k8s.selectedPod!.podName) ?? null) : null
	);

	const detail = $derived(k8s.selectedNode ? k8s.nodeDetail : k8s.selectedPod ? k8s.podDetail : null);

	// The x-axis ends at the newest bucket's end rather than "now" - same reasoning as HostDetailSheet.
	const range = $derived.by(() => {
		if (!detail || detail.points.length === 0) return null;
		const toMs = new Date(detail.points.at(-1)!.bucketStart).getTime() + detail.bucketWidthSeconds * 1000;
		return { fromMs: toMs - detail.windowMinutes * 60_000, toMs };
	});

	function series<P extends { bucketStart: string }>(points: P[], valueOf: (p: P) => number | null) {
		return points.map((p) => ({ time: new Date(p.bucketStart).getTime(), value: valueOf(p) }));
	}

	const charts = $derived.by<Chart[]>(() => {
		if (k8s.selectedNode && k8s.nodeDetail) {
			const points = k8s.nodeDetail.points;
			return [
				{ label: m.kubernetesPage_cpuCoresChart(), unit: null, points: series(points, (p) => p.cpuCores) },
				{ label: m.kubernetesPage_cpuPercentChart(), unit: '%', points: series(points, (p) => p.cpuPercent) },
				{ label: m.kubernetesPage_memoryChart(), unit: 'By', points: series(points, (p) => p.memoryWorkingSetBytes) },
				{ label: m.kubernetesPage_memoryPercentChart(), unit: '%', points: series(points, (p) => p.memoryPercent) }
			];
		}
		if (k8s.selectedPod && k8s.podDetail) {
			const points = k8s.podDetail.points;
			return [
				{ label: m.kubernetesPage_cpuCoresChart(), unit: null, points: series(points, (p) => p.cpuCores) },
				{ label: m.eventDetail_podCpuOfLimit(), unit: '%', points: series(points, (p) => p.cpuLimitPercent) },
				{ label: m.kubernetesPage_memoryChart(), unit: 'By', points: series(points, (p) => p.memoryWorkingSetBytes) },
				{ label: m.eventDetail_podMemoryOfLimit(), unit: '%', points: series(points, (p) => p.memoryLimitPercent) }
			];
		}
		return [];
	});

	const title = $derived(k8s.selectedNode ?? k8s.selectedPod?.podName ?? null);
</script>

<Sheet.Root
	open={title !== null}
	onOpenChange={(next) => {
		if (!next) k8s.closeDetail();
	}}
>
	<Sheet.Content class="flex w-full flex-col data-[side=right]:sm:max-w-3xl">
		{#if title}
			<Sheet.Header>
				<Sheet.Title class="flex flex-wrap items-center gap-2 break-all">
					{title}
					{#if node?.ready === true}
						<Badge variant="outline">{m.kubernetesPage_ready()}</Badge>
					{:else if node?.ready === false}
						<Badge variant="destructive">{m.kubernetesPage_notReady()}</Badge>
					{/if}
					{#if pod?.phase}
						<Badge variant={phaseVariant(pod.phase)}>{pod.phase}</Badge>
					{/if}
				</Sheet.Title>
				<Sheet.Description>{servicesWindowPresetLabel(k8s.windowPreset)}</Sheet.Description>
				<dl class="text-muted-foreground mt-1 grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 text-xs">
					{#if node?.clusterName}
						<dt>{m.kubernetesPage_clusterLabel()}</dt>
						<dd class="text-foreground">{node.clusterName}</dd>
					{/if}
					{#if k8s.selectedNode && k8s.nodeDetail?.allocatableCpuCores != null}
						<dt>{m.kubernetesPage_allocatableCpuLabel()}</dt>
						<dd class="text-foreground tabular-nums">{formatCores(k8s.nodeDetail.allocatableCpuCores)}</dd>
					{/if}
					{#if k8s.selectedPod}
						<dt>{m.kubernetesPage_namespaceColumn()}</dt>
						<dd class="text-foreground">{k8s.selectedPod.namespace}</dd>
					{/if}
					{#if pod?.workloadName}
						<dt>{m.kubernetesPage_workloadColumn()}</dt>
						<dd class="text-foreground">{pod.workloadKind} {pod.workloadName}</dd>
					{/if}
					{#if pod?.nodeName}
						<dt>{m.kubernetesPage_nodeColumn()}</dt>
						<dd>
							<button type="button" class="text-foreground hover:underline" onclick={() => k8s.openNode(pod.nodeName!)}>{pod.nodeName}</button>
						</dd>
					{/if}
					{#if pod?.restarts != null}
						<dt>{m.kubernetesPage_restartsColumn()}</dt>
						<dd class="tabular-nums {pod.restarts ? 'text-warning' : 'text-foreground'}">{pod.restarts}</dd>
					{/if}
				</dl>
				{#if k8s.selectedNode}
					<div>
						<Button variant="outline" size="sm" class="mt-2" onclick={() => k8s.showPodsOnNode(k8s.selectedNode!)}>
							{m.kubernetesPage_viewPodsOnNode()}
						</Button>
					</div>
				{/if}
			</Sheet.Header>
			<div class="min-h-0 flex-1 overflow-y-auto px-4 pb-8">
				{#if k8s.detailLoading && !detail}
					<div class="flex h-32 items-center justify-center"><Spinner /></div>
				{:else if k8s.detailError}
					<p class="text-destructive text-sm">{k8s.detailError}</p>
				{:else if !range}
					<p class="text-muted-foreground text-sm">{m.hostsPage_chartNoData()}</p>
				{:else}
					<div class="grid gap-3 md:grid-cols-2">
						{#each charts as chart (chart.label)}
							<HostMetricChart label={chart.label} unit={chart.unit} points={chart.points} fromMs={range.fromMs} toMs={range.toMs} />
						{/each}
					</div>
				{/if}
			</div>
		{/if}
	</Sheet.Content>
</Sheet.Root>
