// Central reactive state for the Kubernetes page (routes/kubernetes) - the Nodes, Namespaces,
// Workloads, Pods and Volumes tables built from ingested OTel kubeletstats/k8s_cluster
// metrics, plus one node's, workload's, pod's or volume's drill-down. Distinct from the
// Resources page's Kubernetes provider, which polls the Kubernetes API for Flare's own pods only.
//
// Same 30s poll, window presets and load/reload split as HostsState; only the active tab's
// table is fetched, so switching tabs is a load and a hidden tab costs nothing.

import {
	getKubernetesNodeMetrics,
	getKubernetesVolumeMetrics,
	getKubernetesWorkloadMetrics,
	listKubernetesNamespaces,
	listKubernetesNodes,
	listKubernetesPods,
	listKubernetesVolumes,
	listKubernetesWorkloads,
	type KubernetesNamespaceSummary,
	type KubernetesNodeMetricsResponse,
	type KubernetesNodeSummary,
	type KubernetesPodSummary,
	type KubernetesVolumeMetricsResponse,
	type KubernetesVolumeRef,
	type KubernetesVolumeSummary,
	type KubernetesWorkloadKind,
	type KubernetesWorkloadMetricsResponse,
	type KubernetesWorkloadRef,
	type KubernetesWorkloadSummary
} from '$lib/kubernetes-api';
import { getPodMetrics, type PodMetricsResponse } from '$lib/pods-api';
import { HOSTS_WINDOW_PRESETS, type HostsWindowPreset } from '$lib/hosts/state.svelte';
import { workloadStatusRank } from './format';

export const KUBERNETES_TABS = ['nodes', 'namespaces', 'workloads', 'pods', 'volumes'] as const;
export type KubernetesTab = (typeof KUBERNETES_TABS)[number];
export type KubernetesWindowPreset = HostsWindowPreset;
export const KUBERNETES_WINDOW_PRESETS = HOSTS_WINDOW_PRESETS;

export type NodesSortColumn = 'nodeName' | 'ready' | 'cpuCores' | 'cpuPercent' | 'memoryWorkingSetBytes' | 'memoryPercent' | 'podCount' | 'lastSeen';
export type PodsSortColumn = 'podName' | 'namespace' | 'nodeName' | 'phase' | 'restarts' | 'cpuCores' | 'memoryWorkingSetBytes' | 'lastSeen';
export type NamespacesSortColumn = 'namespace' | 'phase' | 'podCount' | 'cpuCores' | 'memoryWorkingSetBytes' | 'lastSeen';
/** `status` sorts by workloadStatusRank - the most degraded first when descending. */
export type WorkloadsSortColumn = 'name' | 'namespace' | 'status' | 'podCount' | 'cpuCores' | 'memoryWorkingSetBytes' | 'lastSeen';
export type VolumesSortColumn = 'volumeName' | 'namespace' | 'podName' | 'usedBytes' | 'capacityBytes' | 'usedPercent' | 'inodesUsedPercent' | 'lastSeen';

const TEXT_COLUMNS = new Set<string>(['nodeName', 'podName', 'namespace', 'phase', 'name', 'volumeName', 'lastSeen']);

const POLL_INTERVAL_MS = 30_000;
const SEARCH_DEBOUNCE_MS = 300;

export interface PodRef {
	namespace: string;
	podName: string;
}

/** Text columns compare as strings (ISO timestamps sort correctly that way); a missing value sorts last in either direction, same as HostsState.sorted. */
function sortRows<T>(rows: T[], valueOf: (row: T) => unknown, text: boolean, descending: boolean): T[] {
	const direction = descending ? -1 : 1;
	return [...rows].sort((a, b) => {
		const left = valueOf(a);
		const right = valueOf(b);
		if (left == null && right == null) return 0;
		if (left == null) return 1;
		if (right == null) return -1;
		if (text) return direction * String(left).localeCompare(String(right));
		return direction * (Number(left) - Number(right));
	});
}

function sortByColumn<T, K extends keyof T & string>(rows: T[], column: K, descending: boolean): T[] {
	return sortRows(rows, (row) => row[column], TEXT_COLUMNS.has(column), descending);
}

/** Names/text A-Z; every figure busiest/most-recent first. */
function defaultDescending(column: string): boolean {
	return !TEXT_COLUMNS.has(column) || column === 'lastSeen';
}

function addKnown(known: string[], values: (string | null)[]): string[] {
	const set = new Set(known);
	const before = set.size;
	for (const value of values) if (value) set.add(value);
	return set.size === before ? known : [...set].sort();
}

export class KubernetesState {
	tab = $state<KubernetesTab>('nodes');
	windowPreset = $state<KubernetesWindowPreset>('1h');
	/** Epoch ms of the last successful load - the "now" for "last seen N min ago" labels. */
	loadedAt = $state(0);
	loading = $state(false);
	error = $state<string | null>(null);

	/** Each tab's own search box. */
	search = $state<Record<KubernetesTab, string>>({ nodes: '', namespaces: '', workloads: '', pods: '', volumes: '' });
	/** Empty string = all namespaces. Shared by the Workloads, Pods and Volumes tabs. */
	namespace = $state('');
	knownNamespaces = $state.raw<string[]>([]);

	// Nodes tab
	/** Empty string = all clusters. */
	clusterName = $state('');
	nodes = $state.raw<KubernetesNodeSummary[] | null>(null);
	nodesTruncated = $state(false);
	/** Every cluster name seen so far - same "don't shrink the picker once filtered" reasoning as HostsState.knownOsTypes. */
	knownClusters = $state.raw<string[]>([]);
	nodesSortColumn = $state<NodesSortColumn>('nodeName');
	nodesSortDescending = $state(false);

	// Namespaces tab
	namespaces = $state.raw<KubernetesNamespaceSummary[] | null>(null);
	namespacesTruncated = $state(false);
	namespacesSortColumn = $state<NamespacesSortColumn>('namespace');
	namespacesSortDescending = $state(false);

	// Workloads tab
	workloadKind = $state<KubernetesWorkloadKind>('Deployment');
	workloads = $state.raw<KubernetesWorkloadSummary[] | null>(null);
	/** The kind `workloads` was loaded for - the table renders by this, not `workloadKind`, so a kind switch never shows the old rows under the new kind's columns. */
	workloadsKind = $state<KubernetesWorkloadKind>('Deployment');
	workloadsTruncated = $state(false);
	workloadsSortColumn = $state<WorkloadsSortColumn>('namespace');
	workloadsSortDescending = $state(false);

	// Pods tab
	/** Empty string = all nodes. */
	nodeFilter = $state('');
	/** The Workloads tab's "View pods" - null = any workload. */
	workloadFilter = $state<KubernetesWorkloadRef | null>(null);
	pods = $state.raw<KubernetesPodSummary[] | null>(null);
	podsTruncated = $state(false);
	knownNodes = $state.raw<string[]>([]);
	podsSortColumn = $state<PodsSortColumn>('namespace');
	podsSortDescending = $state(false);

	// Volumes tab
	volumes = $state.raw<KubernetesVolumeSummary[] | null>(null);
	volumesTruncated = $state(false);
	volumesSortColumn = $state<VolumesSortColumn>('usedPercent');
	volumesSortDescending = $state(true);

	// Drill-downs - at most one sheet open at a time.
	selectedNode = $state<string | null>(null);
	nodeDetail = $state.raw<KubernetesNodeMetricsResponse | null>(null);
	selectedPod = $state<PodRef | null>(null);
	podDetail = $state.raw<PodMetricsResponse | null>(null);
	selectedWorkload = $state<KubernetesWorkloadRef | null>(null);
	workloadDetail = $state.raw<KubernetesWorkloadMetricsResponse | null>(null);
	selectedVolume = $state<KubernetesVolumeRef | null>(null);
	volumeDetail = $state.raw<KubernetesVolumeMetricsResponse | null>(null);
	detailLoading = $state(false);
	detailError = $state<string | null>(null);

	#abort: AbortController | null = null;
	#detailAbort: AbortController | null = null;
	#pollHandle: ReturnType<typeof setInterval> | null = null;
	#searchHandle: ReturnType<typeof setTimeout> | null = null;

	minutes(): number {
		return KUBERNETES_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 60;
	}

	#rows(tab: KubernetesTab): unknown[] | null {
		switch (tab) {
			case 'nodes':
				return this.nodes;
			case 'namespaces':
				return this.namespaces;
			case 'workloads':
				return this.workloads;
			case 'pods':
				return this.pods;
			case 'volumes':
				return this.volumes;
		}
	}

	#clear(tab: KubernetesTab): void {
		switch (tab) {
			case 'nodes':
				this.nodes = null;
				break;
			case 'namespaces':
				this.namespaces = null;
				break;
			case 'workloads':
				this.workloads = null;
				break;
			case 'pods':
				this.pods = null;
				break;
			case 'volumes':
				this.volumes = null;
				break;
		}
	}

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		const tab = this.tab;
		if (this.#rows(tab) == null) this.loading = true;
		this.error = null;
		const minutes = this.minutes();
		const search = this.search[tab];
		try {
			switch (tab) {
				case 'nodes': {
					const response = await listKubernetesNodes(minutes, { search, clusterName: this.clusterName }, abort.signal);
					if (abort.signal.aborted) return;
					this.nodes = response.nodes;
					this.nodesTruncated = response.truncated;
					this.knownClusters = addKnown(this.knownClusters, response.nodes.map((n) => n.clusterName));
					this.knownNodes = addKnown(this.knownNodes, response.nodes.map((n) => n.nodeName));
					break;
				}
				case 'namespaces': {
					const response = await listKubernetesNamespaces(minutes, search, abort.signal);
					if (abort.signal.aborted) return;
					this.namespaces = response.namespaces;
					this.namespacesTruncated = response.truncated;
					this.knownNamespaces = addKnown(this.knownNamespaces, response.namespaces.map((n) => n.namespace));
					break;
				}
				case 'workloads': {
					const kind = this.workloadKind;
					const response = await listKubernetesWorkloads(kind, minutes, { search, namespace: this.namespace }, abort.signal);
					if (abort.signal.aborted) return;
					this.workloads = response.workloads;
					this.workloadsKind = kind;
					this.workloadsTruncated = response.truncated;
					this.knownNamespaces = addKnown(this.knownNamespaces, response.workloads.map((w) => w.namespace));
					break;
				}
				case 'pods': {
					const response = await listKubernetesPods(
						minutes,
						{ search, namespace: this.namespace, nodeName: this.nodeFilter, workload: this.workloadFilter },
						abort.signal
					);
					if (abort.signal.aborted) return;
					this.pods = response.pods;
					this.podsTruncated = response.truncated;
					this.knownNamespaces = addKnown(this.knownNamespaces, response.pods.map((p) => p.namespace));
					this.knownNodes = addKnown(this.knownNodes, response.pods.map((p) => p.nodeName));
					break;
				}
				case 'volumes': {
					const response = await listKubernetesVolumes(minutes, { search, namespace: this.namespace }, abort.signal);
					if (abort.signal.aborted) return;
					this.volumes = response.volumes;
					this.volumesTruncated = response.truncated;
					this.knownNamespaces = addKnown(this.knownNamespaces, response.volumes.map((v) => v.namespace));
					break;
				}
			}
			this.loadedAt = Date.now();
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}

		if (this.#hasSelection()) void this.#loadDetail(false);
	}

	/** A filter/window change is a genuinely different query - clear the active table and force the spinner, same as HostsState. */
	#reload(): void {
		this.#clear(this.tab);
		void this.load();
	}

	setTab(tab: KubernetesTab): void {
		if (this.tab === tab) return;
		this.tab = tab;
		this.error = null;
		void this.load();
	}

	setWindowPreset(preset: KubernetesWindowPreset): void {
		if (this.windowPreset === preset) return;
		this.windowPreset = preset;
		// Every table is for the old window now; the inactive ones reload when shown.
		for (const tab of KUBERNETES_TABS) this.#clear(tab);
		this.nodeDetail = null;
		this.podDetail = null;
		this.workloadDetail = null;
		this.volumeDetail = null;
		void this.load();
	}

	setClusterName(clusterName: string): void {
		if (this.clusterName === clusterName) return;
		this.clusterName = clusterName;
		this.#reload();
	}

	setNamespace(namespace: string): void {
		if (this.namespace === namespace) return;
		this.namespace = namespace;
		// Shared by three tabs - the inactive ones are stale too.
		this.workloads = null;
		this.pods = null;
		this.volumes = null;
		void this.load();
	}

	setNodeFilter(nodeName: string): void {
		if (this.nodeFilter === nodeName) return;
		this.nodeFilter = nodeName;
		this.#reload();
	}

	setWorkloadFilter(workload: KubernetesWorkloadRef | null): void {
		this.workloadFilter = workload;
		this.#reload();
	}

	setWorkloadKind(kind: KubernetesWorkloadKind): void {
		if (this.workloadKind === kind) return;
		this.workloadKind = kind;
		// Status means something different per kind - restart from the default order.
		if (this.workloadsSortColumn === 'status') {
			this.workloadsSortColumn = 'namespace';
			this.workloadsSortDescending = false;
		}
		this.#reload();
	}

	/** Debounced - fires on every keystroke from the active tab's search box. */
	setSearch(search: string): void {
		this.search[this.tab] = search;
		if (this.#searchHandle !== null) clearTimeout(this.#searchHandle);
		this.#searchHandle = setTimeout(() => {
			this.#searchHandle = null;
			this.#reload();
		}, SEARCH_DEBOUNCE_MS);
	}

	/** Jumps to the Pods tab with only the given pod filters set. */
	#showPods(filters: { namespace?: string; nodeName?: string; workload?: KubernetesWorkloadRef }): void {
		this.closeDetail();
		this.search.pods = '';
		const namespace = filters.namespace ?? '';
		if (this.namespace !== namespace) {
			this.namespace = namespace;
			this.workloads = null;
			this.volumes = null;
		}
		this.nodeFilter = filters.nodeName ?? '';
		this.workloadFilter = filters.workload ?? null;
		this.pods = null;
		this.tab = 'pods';
		void this.load();
	}

	/** The node sheet's "View pods". */
	showPodsOnNode(nodeName: string): void {
		this.#showPods({ nodeName });
	}

	/** The workload sheet's "View pods". */
	showPodsForWorkload(workload: KubernetesWorkloadRef): void {
		this.#showPods({ namespace: workload.namespace, workload });
	}

	/** A Namespaces-table row click. */
	showPodsInNamespace(namespace: string): void {
		this.#showPods({ namespace });
	}

	setNodesSort(column: NodesSortColumn): void {
		if (this.nodesSortColumn === column) {
			this.nodesSortDescending = !this.nodesSortDescending;
		} else {
			this.nodesSortColumn = column;
			// "Ready" ascending puts not-ready (false) nodes first - the ones worth looking at.
			this.nodesSortDescending = column !== 'nodeName' && column !== 'ready';
		}
	}

	setPodsSort(column: PodsSortColumn): void {
		if (this.podsSortColumn === column) {
			this.podsSortDescending = !this.podsSortDescending;
		} else {
			this.podsSortColumn = column;
			this.podsSortDescending = defaultDescending(column);
		}
	}

	setNamespacesSort(column: NamespacesSortColumn): void {
		if (this.namespacesSortColumn === column) {
			this.namespacesSortDescending = !this.namespacesSortDescending;
		} else {
			this.namespacesSortColumn = column;
			this.namespacesSortDescending = defaultDescending(column);
		}
	}

	setWorkloadsSort(column: WorkloadsSortColumn): void {
		if (this.workloadsSortColumn === column) {
			this.workloadsSortDescending = !this.workloadsSortDescending;
		} else {
			this.workloadsSortColumn = column;
			this.workloadsSortDescending = defaultDescending(column);
		}
	}

	setVolumesSort(column: VolumesSortColumn): void {
		if (this.volumesSortColumn === column) {
			this.volumesSortDescending = !this.volumesSortDescending;
		} else {
			this.volumesSortColumn = column;
			this.volumesSortDescending = defaultDescending(column);
		}
	}

	sortedNodes(): KubernetesNodeSummary[] {
		return sortByColumn(this.nodes ?? [], this.nodesSortColumn, this.nodesSortDescending);
	}

	sortedPods(): KubernetesPodSummary[] {
		const rows = sortByColumn(this.pods ?? [], this.podsSortColumn, this.podsSortDescending);
		// Namespace sort ties on pod name, so the default order reads namespace-then-pod.
		if (this.podsSortColumn !== 'namespace') return rows;
		const direction = this.podsSortDescending ? -1 : 1;
		return rows.sort((a, b) => direction * a.namespace.localeCompare(b.namespace) || a.podName.localeCompare(b.podName));
	}

	sortedNamespaces(): KubernetesNamespaceSummary[] {
		return sortByColumn(this.namespaces ?? [], this.namespacesSortColumn, this.namespacesSortDescending);
	}

	sortedWorkloads(): KubernetesWorkloadSummary[] {
		const rows = this.workloads ?? [];
		const kind = this.workloadsKind;
		if (this.workloadsSortColumn === 'status') {
			return sortRows(rows, (w) => workloadStatusRank(kind, w), false, this.workloadsSortDescending);
		}
		const sorted = sortByColumn(rows, this.workloadsSortColumn, this.workloadsSortDescending);
		if (this.workloadsSortColumn !== 'namespace') return sorted;
		const direction = this.workloadsSortDescending ? -1 : 1;
		return sorted.sort((a, b) => direction * a.namespace.localeCompare(b.namespace) || a.name.localeCompare(b.name));
	}

	sortedVolumes(): KubernetesVolumeSummary[] {
		return sortByColumn(this.volumes ?? [], this.volumesSortColumn, this.volumesSortDescending);
	}

	openNode(nodeName: string): void {
		this.closeDetail();
		this.selectedNode = nodeName;
		void this.#loadDetail(true);
	}

	openPod(pod: PodRef): void {
		this.closeDetail();
		this.selectedPod = pod;
		void this.#loadDetail(true);
	}

	openWorkload(workload: KubernetesWorkloadRef): void {
		this.closeDetail();
		this.selectedWorkload = workload;
		void this.#loadDetail(true);
	}

	openVolume(volume: KubernetesVolumeRef): void {
		this.closeDetail();
		this.selectedVolume = volume;
		void this.#loadDetail(true);
	}

	closeDetail(): void {
		this.#detailAbort?.abort();
		this.selectedNode = null;
		this.selectedPod = null;
		this.selectedWorkload = null;
		this.selectedVolume = null;
		this.nodeDetail = null;
		this.podDetail = null;
		this.workloadDetail = null;
		this.volumeDetail = null;
		this.detailError = null;
		this.detailLoading = false;
	}

	#hasSelection(): boolean {
		return this.selectedNode != null || this.selectedPod != null || this.selectedWorkload != null || this.selectedVolume != null;
	}

	async #loadDetail(showSpinner: boolean): Promise<void> {
		const node = this.selectedNode;
		const pod = this.selectedPod;
		const workload = this.selectedWorkload;
		const volume = this.selectedVolume;
		if (!this.#hasSelection()) return;

		this.#detailAbort?.abort();
		const abort = new AbortController();
		this.#detailAbort = abort;

		if (showSpinner) this.detailLoading = true;
		this.detailError = null;
		const minutes = this.minutes();
		try {
			if (node != null) {
				const detail = await getKubernetesNodeMetrics(node, minutes, abort.signal);
				if (!abort.signal.aborted) this.nodeDetail = detail;
			} else if (pod != null) {
				const detail = await getPodMetrics({ podName: pod.podName, namespace: pod.namespace, windowMinutes: minutes }, abort.signal);
				if (!abort.signal.aborted) this.podDetail = detail;
			} else if (workload != null) {
				const detail = await getKubernetesWorkloadMetrics(workload, minutes, abort.signal);
				if (!abort.signal.aborted) this.workloadDetail = detail;
			} else if (volume != null) {
				const detail = await getKubernetesVolumeMetrics(volume, minutes, abort.signal);
				if (!abort.signal.aborted) this.volumeDetail = detail;
			}
		} catch (err) {
			if (abort.signal.aborted) return;
			this.detailError = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.detailLoading = false;
		}
	}

	startPolling(): void {
		this.stopPolling();
		this.#pollHandle = setInterval(() => void this.load(), POLL_INTERVAL_MS);
	}

	stopPolling(): void {
		if (this.#pollHandle !== null) {
			clearInterval(this.#pollHandle);
			this.#pollHandle = null;
		}
	}

	dispose(): void {
		this.stopPolling();
		if (this.#searchHandle !== null) clearTimeout(this.#searchHandle);
		this.#abort?.abort();
		this.#detailAbort?.abort();
	}
}
