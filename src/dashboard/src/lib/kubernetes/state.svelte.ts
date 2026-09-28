// Central reactive state for the Kubernetes page (routes/kubernetes) - the Nodes and Pods
// tables built from ingested OTel kubeletstats/k8s_cluster metrics, plus one node's or
// pod's drill-down. Distinct from the Resources page's Kubernetes provider, which polls the
// Kubernetes API for Flare's own pods only.
//
// Same 30s poll, window presets and load/reload split as HostsState; only the active tab's
// table is fetched, so switching tabs is a load and a hidden tab costs nothing.

import {
	getKubernetesNodeMetrics,
	listKubernetesNodes,
	listKubernetesPods,
	type KubernetesNodeMetricsResponse,
	type KubernetesNodeSummary,
	type KubernetesPodSummary
} from '$lib/kubernetes-api';
import { getPodMetrics, type PodMetricsResponse } from '$lib/pods-api';
import { HOSTS_WINDOW_PRESETS, type HostsWindowPreset } from '$lib/hosts/state.svelte';

export type KubernetesTab = 'nodes' | 'pods';
export type KubernetesWindowPreset = HostsWindowPreset;
export const KUBERNETES_WINDOW_PRESETS = HOSTS_WINDOW_PRESETS;

export type NodesSortColumn = 'nodeName' | 'ready' | 'cpuCores' | 'cpuPercent' | 'memoryWorkingSetBytes' | 'memoryPercent' | 'podCount' | 'lastSeen';
export type PodsSortColumn = 'podName' | 'namespace' | 'nodeName' | 'phase' | 'restarts' | 'cpuCores' | 'memoryWorkingSetBytes' | 'lastSeen';

const TEXT_COLUMNS = new Set<string>(['nodeName', 'podName', 'namespace', 'phase', 'lastSeen']);

const POLL_INTERVAL_MS = 30_000;
const SEARCH_DEBOUNCE_MS = 300;

export interface PodRef {
	namespace: string;
	podName: string;
}

/** Text columns compare as strings (ISO timestamps sort correctly that way); a missing value sorts last in either direction, same as HostsState.sorted. */
function sortRows<T, K extends keyof T & string>(rows: T[], column: K, descending: boolean): T[] {
	const direction = descending ? -1 : 1;
	return [...rows].sort((a, b) => {
		const left = a[column] as unknown;
		const right = b[column] as unknown;
		if (left == null && right == null) return 0;
		if (left == null) return 1;
		if (right == null) return -1;
		if (TEXT_COLUMNS.has(column)) return direction * String(left).localeCompare(String(right));
		return direction * (Number(left) - Number(right));
	});
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

	// Nodes tab
	nodeSearch = $state('');
	/** Empty string = all clusters. */
	clusterName = $state('');
	nodes = $state.raw<KubernetesNodeSummary[] | null>(null);
	nodesTruncated = $state(false);
	/** Every cluster name seen so far - same "don't shrink the picker once filtered" reasoning as HostsState.knownOsTypes. */
	knownClusters = $state.raw<string[]>([]);
	nodesSortColumn = $state<NodesSortColumn>('nodeName');
	nodesSortDescending = $state(false);

	// Pods tab
	podSearch = $state('');
	/** Empty string = all namespaces. */
	namespace = $state('');
	/** Empty string = all nodes. */
	nodeFilter = $state('');
	pods = $state.raw<KubernetesPodSummary[] | null>(null);
	podsTruncated = $state(false);
	knownNamespaces = $state.raw<string[]>([]);
	knownNodes = $state.raw<string[]>([]);
	podsSortColumn = $state<PodsSortColumn>('namespace');
	podsSortDescending = $state(false);

	// Drill-downs - at most one sheet open at a time.
	selectedNode = $state<string | null>(null);
	nodeDetail = $state.raw<KubernetesNodeMetricsResponse | null>(null);
	selectedPod = $state<PodRef | null>(null);
	podDetail = $state.raw<PodMetricsResponse | null>(null);
	detailLoading = $state(false);
	detailError = $state<string | null>(null);

	#abort: AbortController | null = null;
	#detailAbort: AbortController | null = null;
	#pollHandle: ReturnType<typeof setInterval> | null = null;
	#searchHandle: ReturnType<typeof setTimeout> | null = null;

	minutes(): number {
		return KUBERNETES_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 60;
	}

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		const tab = this.tab;
		if ((tab === 'nodes' ? this.nodes : this.pods) == null) this.loading = true;
		this.error = null;
		try {
			if (tab === 'nodes') {
				const response = await listKubernetesNodes(this.minutes(), { search: this.nodeSearch, clusterName: this.clusterName }, abort.signal);
				if (abort.signal.aborted) return;
				this.nodes = response.nodes;
				this.nodesTruncated = response.truncated;
				this.knownClusters = addKnown(this.knownClusters, response.nodes.map((n) => n.clusterName));
				this.knownNodes = addKnown(this.knownNodes, response.nodes.map((n) => n.nodeName));
			} else {
				const response = await listKubernetesPods(
					this.minutes(),
					{ search: this.podSearch, namespace: this.namespace, nodeName: this.nodeFilter },
					abort.signal
				);
				if (abort.signal.aborted) return;
				this.pods = response.pods;
				this.podsTruncated = response.truncated;
				this.knownNamespaces = addKnown(this.knownNamespaces, response.pods.map((p) => p.namespace));
				this.knownNodes = addKnown(this.knownNodes, response.pods.map((p) => p.nodeName));
			}
			this.loadedAt = Date.now();
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}

		if (this.selectedNode != null || this.selectedPod != null) void this.#loadDetail(false);
	}

	/** A filter/window change is a genuinely different query - clear the active table and force the spinner, same as HostsState. */
	#reload(): void {
		if (this.tab === 'nodes') this.nodes = null;
		else this.pods = null;
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
		// Both tables are for the old window now; the inactive one reloads when shown.
		this.nodes = null;
		this.pods = null;
		this.nodeDetail = null;
		this.podDetail = null;
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
		this.#reload();
	}

	setNodeFilter(nodeName: string): void {
		if (this.nodeFilter === nodeName) return;
		this.nodeFilter = nodeName;
		this.#reload();
	}

	/** Debounced - fires on every keystroke from the active tab's search box. */
	setSearch(search: string): void {
		if (this.tab === 'nodes') this.nodeSearch = search;
		else this.podSearch = search;
		if (this.#searchHandle !== null) clearTimeout(this.#searchHandle);
		this.#searchHandle = setTimeout(() => {
			this.#searchHandle = null;
			this.#reload();
		}, SEARCH_DEBOUNCE_MS);
	}

	/** The node sheet's "View pods" - the Pods tab filtered to that node, other pod filters cleared. */
	showPodsOnNode(nodeName: string): void {
		this.closeDetail();
		this.podSearch = '';
		this.namespace = '';
		this.nodeFilter = nodeName;
		this.pods = null;
		this.tab = 'pods';
		void this.load();
	}

	setNodesSort(column: NodesSortColumn): void {
		if (this.nodesSortColumn === column) {
			this.nodesSortDescending = !this.nodesSortDescending;
		} else {
			this.nodesSortColumn = column;
			// Names A-Z; every figure busiest/most-recent first. "Ready" ascending puts
			// not-ready (false) nodes first - the ones worth looking at.
			this.nodesSortDescending = column !== 'nodeName' && column !== 'ready';
		}
	}

	setPodsSort(column: PodsSortColumn): void {
		if (this.podsSortColumn === column) {
			this.podsSortDescending = !this.podsSortDescending;
		} else {
			this.podsSortColumn = column;
			this.podsSortDescending = !TEXT_COLUMNS.has(column) || column === 'lastSeen';
		}
	}

	sortedNodes(): KubernetesNodeSummary[] {
		return sortRows(this.nodes ?? [], this.nodesSortColumn, this.nodesSortDescending);
	}

	sortedPods(): KubernetesPodSummary[] {
		const rows = sortRows(this.pods ?? [], this.podsSortColumn, this.podsSortDescending);
		// Namespace sort ties on pod name, so the default order reads namespace-then-pod.
		if (this.podsSortColumn !== 'namespace') return rows;
		const direction = this.podsSortDescending ? -1 : 1;
		return rows.sort((a, b) => direction * a.namespace.localeCompare(b.namespace) || a.podName.localeCompare(b.podName));
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

	closeDetail(): void {
		this.#detailAbort?.abort();
		this.selectedNode = null;
		this.selectedPod = null;
		this.nodeDetail = null;
		this.podDetail = null;
		this.detailError = null;
		this.detailLoading = false;
	}

	async #loadDetail(showSpinner: boolean): Promise<void> {
		const node = this.selectedNode;
		const pod = this.selectedPod;
		if (node == null && pod == null) return;

		this.#detailAbort?.abort();
		const abort = new AbortController();
		this.#detailAbort = abort;

		if (showSpinner) this.detailLoading = true;
		this.detailError = null;
		try {
			if (node != null) {
				const detail = await getKubernetesNodeMetrics(node, this.minutes(), abort.signal);
				if (!abort.signal.aborted) this.nodeDetail = detail;
			} else if (pod != null) {
				const detail = await getPodMetrics({ podName: pod.podName, namespace: pod.namespace, windowMinutes: this.minutes() }, abort.signal);
				if (!abort.signal.aborted) this.podDetail = detail;
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
