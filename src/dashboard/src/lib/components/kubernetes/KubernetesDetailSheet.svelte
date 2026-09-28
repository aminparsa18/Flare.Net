<script lang="ts">
	// One node's, pod's, workload's or volume's drill-down charts - whichever KubernetesState
	// has selected. Same small-charts layout as HostDetailSheet, reusing its HostMetricChart.
	import * as Sheet from '$lib/components/ui/sheet';
	import { Badge } from '$lib/components/ui/badge';
	import { Button, buttonVariants } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import HostMetricChart from '$lib/components/hosts/HostMetricChart.svelte';
	import KubernetesWorkloadStatus from './KubernetesWorkloadStatus.svelte';
	import { kubernetesContext } from '$lib/kubernetes/context';
	import { formatBytes, formatCores, isReplicatedKind, phaseVariant } from '$lib/kubernetes/format';
	import { buildKubernetesPodLogsHref } from '$lib/deep-links';
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
	const workload = $derived.by(() => {
		const selected = k8s.selectedWorkload;
		if (!selected || k8s.workloadsKind !== selected.kind) return null;
		return k8s.workloads?.find((w) => w.namespace === selected.namespace && w.name === selected.name) ?? null;
	});
	const volume = $derived.by(() => {
		const selected = k8s.selectedVolume;
		if (!selected) return null;
		return (
			k8s.volumes?.find((v) => v.namespace === selected.namespace && v.podName === selected.podName && v.volumeName === selected.volumeName) ?? null
		);
	});

	const detail = $derived(
		k8s.selectedNode ? k8s.nodeDetail : k8s.selectedPod ? k8s.podDetail : k8s.selectedWorkload ? k8s.workloadDetail : k8s.selectedVolume ? k8s.volumeDetail : null
	);

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
		if (k8s.selectedWorkload && k8s.workloadDetail) {
			const kind = k8s.selectedWorkload.kind;
			const points = k8s.workloadDetail.points;
			const counts: Chart[] = isReplicatedKind(kind)
				? [
						{ label: m.kubernetesPage_readyLabel(), unit: null, points: series(points, (p) => p.ready) },
						{ label: m.kubernetesPage_desiredLabel(), unit: null, points: series(points, (p) => p.desired) },
						...(kind === 'Deployment' ? [] : [{ label: m.kubernetesPage_currentLabel(), unit: null, points: series(points, (p) => p.current) }])
					]
				: kind === 'Job'
					? [
							{ label: m.kubernetesPage_activeLabel(), unit: null, points: series(points, (p) => p.active) },
							{ label: m.kubernetesPage_succeededLabel(), unit: null, points: series(points, (p) => p.succeeded) },
							{ label: m.kubernetesPage_failedLabel(), unit: null, points: series(points, (p) => p.failed) }
						]
					: [{ label: m.kubernetesPage_activeLabel(), unit: null, points: series(points, (p) => p.active) }];
			return [
				...counts,
				{ label: m.kubernetesPage_cpuCoresChart(), unit: null, points: series(points, (p) => p.cpuCores) },
				{ label: m.kubernetesPage_memoryChart(), unit: 'By', points: series(points, (p) => p.memoryWorkingSetBytes) }
			];
		}
		if (k8s.selectedVolume && k8s.volumeDetail) {
			const points = k8s.volumeDetail.points;
			return [
				{ label: m.kubernetesPage_usedChart(), unit: 'By', points: series(points, (p) => p.usedBytes) },
				{ label: m.kubernetesPage_usedPercentChart(), unit: '%', points: series(points, (p) => p.usedPercent) },
				{ label: m.kubernetesPage_inodesChart(), unit: '%', points: series(points, (p) => p.inodesUsedPercent) }
			];
		}
		return [];
	});

	const title = $derived(
		k8s.selectedNode ?? k8s.selectedPod?.podName ?? k8s.selectedWorkload?.name ?? k8s.selectedVolume?.volumeName ?? null
	);
	/** The pod whose namespace/node/logs the header shows - the selected pod, or a selected volume's pod. */
	const podRef = $derived(k8s.selectedPod ?? (k8s.selectedVolume ? { namespace: k8s.selectedVolume.namespace, podName: k8s.selectedVolume.podName } : null));
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
					{#if k8s.selectedWorkload}
						<span class="text-muted-foreground text-sm font-normal">{k8s.selectedWorkload.kind}</span>
					{/if}
					{title}
					{#if node?.ready === true}
						<Badge variant="outline">{m.kubernetesPage_ready()}</Badge>
					{:else if node?.ready === false}
						<Badge variant="destructive">{m.kubernetesPage_notReady()}</Badge>
					{/if}
					{#if pod?.phase}
						<Badge variant={phaseVariant(pod.phase)}>{pod.phase}</Badge>
					{/if}
					{#if k8s.selectedWorkload && workload}
						<KubernetesWorkloadStatus kind={k8s.selectedWorkload.kind} counts={workload} />
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
					{#if podRef ?? k8s.selectedWorkload}
						<dt>{m.kubernetesPage_namespaceColumn()}</dt>
						<dd class="text-foreground">{(podRef ?? k8s.selectedWorkload)!.namespace}</dd>
					{/if}
					{#if k8s.selectedVolume}
						<dt>{m.kubernetesPage_podColumn()}</dt>
						<dd>
							<button type="button" class="text-foreground hover:underline" onclick={() => podRef && k8s.openPod(podRef)}>
								{k8s.selectedVolume.podName}
							</button>
						</dd>
						{#if volume?.claimName}
							<dt>{m.kubernetesPage_claimColumn()}</dt>
							<dd class="text-foreground">{volume.claimName}</dd>
						{/if}
						{#if volume?.volumeType}
							<dt>{m.kubernetesPage_typeColumn()}</dt>
							<dd class="text-foreground">{volume.volumeType}</dd>
						{/if}
						{#if volume?.capacityBytes != null}
							<dt>{m.kubernetesPage_capacityColumn()}</dt>
							<dd class="text-foreground tabular-nums">{formatBytes(volume.capacityBytes)}</dd>
						{/if}
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
				<div class="mt-2 flex flex-wrap gap-2">
					{#if k8s.selectedNode}
						<Button variant="outline" size="sm" onclick={() => k8s.showPodsOnNode(k8s.selectedNode!)}>
							{m.kubernetesPage_viewPodsOnNode()}
						</Button>
					{/if}
					{#if k8s.selectedWorkload}
						<Button variant="outline" size="sm" onclick={() => k8s.showPodsForWorkload(k8s.selectedWorkload!)}>
							{m.kubernetesPage_viewPods()}
						</Button>
					{/if}
					{#if k8s.selectedPod}
						<a class={buttonVariants({ variant: 'outline', size: 'sm' })} href={buildKubernetesPodLogsHref(k8s.selectedPod, k8s.windowPreset)}>
							{m.kubernetesPage_viewLogs()}
						</a>
					{/if}
				</div>
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
