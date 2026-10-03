// Client for Flare.Api's Kubernetes page endpoints (src/Flare.Api/Endpoints/
// KubernetesInventoryEndpoints.cs): the Nodes table (`POST /api/kubernetes/nodes`), one
// node's drill-down (`POST /api/kubernetes/nodes/metrics`), the Pods table
// (`POST /api/kubernetes/pods`) and the Workloads/Namespaces/Volumes tables and their
// drill-downs (`/api/kubernetes/workloads`, `/namespaces`, `/volumes`), all derived from
// ingested OTel kubeletstats/k8s_cluster metrics - see KubernetesInventoryQueryBuilder.cs and
// KubernetesWorkloadQueryBuilder.cs for which metric feeds which field. A pod's drill-down is
// `pods-api.ts`' getPodMetrics.
//
// MemoryPack over the wire, same shape as `hosts-api.ts`: the requests are generated
// classes; the responses carry a DateTimeOffset inside an IReadOnlyList, so they're
// hand-written under `$lib/memorypack/`.

import { API_BASE_URL, apiFetch, memoryPackBody, memoryPackRequestHeaders } from './api';
import { KubernetesNodeListRequest as GeneratedNodeListRequest } from '$lib/generated/memorypack/KubernetesNodeListRequest.js';
import { KubernetesNodeMetricsRequest as GeneratedNodeMetricsRequest } from '$lib/generated/memorypack/KubernetesNodeMetricsRequest.js';
import { KubernetesPodListRequest as GeneratedPodListRequest } from '$lib/generated/memorypack/KubernetesPodListRequest.js';
import { KubernetesNodeListResponse as GeneratedNodeListResponse } from '$lib/memorypack/KubernetesNodeListResponse';
import { KubernetesNodeMetricsResponse as GeneratedNodeMetricsResponse } from '$lib/memorypack/KubernetesNodeMetricsResponse';
import { KubernetesPodListResponse as GeneratedPodListResponse } from '$lib/memorypack/KubernetesPodListResponse';
import { KubernetesWorkloadListRequest as GeneratedWorkloadListRequest } from '$lib/generated/memorypack/KubernetesWorkloadListRequest.js';
import { KubernetesWorkloadMetricsRequest as GeneratedWorkloadMetricsRequest } from '$lib/generated/memorypack/KubernetesWorkloadMetricsRequest.js';
import { KubernetesNamespaceListRequest as GeneratedNamespaceListRequest } from '$lib/generated/memorypack/KubernetesNamespaceListRequest.js';
import { KubernetesVolumeListRequest as GeneratedVolumeListRequest } from '$lib/generated/memorypack/KubernetesVolumeListRequest.js';
import { KubernetesVolumeMetricsRequest as GeneratedVolumeMetricsRequest } from '$lib/generated/memorypack/KubernetesVolumeMetricsRequest.js';
import { KubernetesWorkloadListResponse as GeneratedWorkloadListResponse } from '$lib/memorypack/KubernetesWorkloadListResponse';
import { KubernetesWorkloadMetricsResponse as GeneratedWorkloadMetricsResponse } from '$lib/memorypack/KubernetesWorkloadMetricsResponse';
import { KubernetesNamespaceListResponse as GeneratedNamespaceListResponse } from '$lib/memorypack/KubernetesNamespaceListResponse';
import { KubernetesVolumeListResponse as GeneratedVolumeListResponse } from '$lib/memorypack/KubernetesVolumeListResponse';
import { KubernetesVolumeMetricsResponse as GeneratedVolumeMetricsResponse } from '$lib/memorypack/KubernetesVolumeMetricsResponse';

/** Usage figures are window averages; null = no data for that metric (never 0). */
export interface KubernetesNodeSummary {
	nodeName: string;
	clusterName: string | null;
	/** Latest `k8s.node.condition_ready`; null = unknown or not reported. */
	ready: boolean | null;
	cpuCores: number | null;
	/** Of allocatable CPU - null unless the k8s_cluster receiver reports allocatable CPU. */
	cpuPercent: number | null;
	memoryWorkingSetBytes: number | null;
	memoryPercent: number | null;
	podCount: number | null;
	lastSeen: string;
}

export interface KubernetesNodeListResponse {
	windowMinutes: number;
	nodes: KubernetesNodeSummary[];
	truncated: boolean;
}

export interface KubernetesNodeMetricsPoint {
	bucketStart: string;
	cpuCores: number | null;
	cpuPercent: number | null;
	memoryWorkingSetBytes: number | null;
	memoryPercent: number | null;
}

export interface KubernetesNodeMetricsResponse {
	nodeName: string;
	windowMinutes: number;
	bucketWidthSeconds: number;
	allocatableCpuCores: number | null;
	points: KubernetesNodeMetricsPoint[];
}

/** Phase/restarts are the latest readings (k8s_cluster receiver); usage figures are window averages (kubeletstats). */
export interface KubernetesPodSummary {
	podName: string;
	namespace: string;
	nodeName: string | null;
	workloadKind: string | null;
	workloadName: string | null;
	phase: string | null;
	restarts: number | null;
	cpuCores: number | null;
	memoryWorkingSetBytes: number | null;
	cpuLimitPercent: number | null;
	memoryLimitPercent: number | null;
	lastSeen: string;
}

export interface KubernetesPodListResponse {
	windowMinutes: number;
	pods: KubernetesPodSummary[];
	truncated: boolean;
}

export interface KubernetesNodeFilter {
	search?: string;
	clusterName?: string;
}

export interface KubernetesPodFilter {
	search?: string;
	namespace?: string;
	nodeName?: string;
	/** Pods carrying this workload's name attribute - see KubernetesPodListRequest.WorkloadKind. */
	workload?: KubernetesWorkloadRef | null;
}

/** KubernetesWorkloadQueryBuilder.Kinds - ReplicaSets deliberately excluded. */
export const KUBERNETES_WORKLOAD_KINDS = ['Deployment', 'StatefulSet', 'DaemonSet', 'Job', 'CronJob'] as const;
export type KubernetesWorkloadKind = (typeof KUBERNETES_WORKLOAD_KINDS)[number];

export interface KubernetesWorkloadRef {
	kind: KubernetesWorkloadKind;
	namespace: string;
	name: string;
}

/**
 * Counts are the latest k8s_cluster readings and only the ones the kind reports are ever set
 * (desired/ready for Deployment, + current for StatefulSet/DaemonSet, misscheduled for DaemonSet, desired/active/
 * succeeded/failed for Job, active for CronJob); pod count and usage need kubeletstats plus
 * the k8sattributes processor tagging pods with their workload.
 */
export interface KubernetesWorkloadCounts {
	desired: number | null;
	ready: number | null;
	current: number | null;
	misscheduled: number | null;
	active: number | null;
	succeeded: number | null;
	failed: number | null;
}

export interface KubernetesWorkloadSummary extends KubernetesWorkloadCounts {
	name: string;
	namespace: string;
	podCount: number | null;
	cpuCores: number | null;
	memoryWorkingSetBytes: number | null;
	lastSeen: string;
}

export interface KubernetesWorkloadListResponse {
	kind: string;
	windowMinutes: number;
	workloads: KubernetesWorkloadSummary[];
	truncated: boolean;
}

export interface KubernetesWorkloadMetricsPoint extends KubernetesWorkloadCounts {
	bucketStart: string;
	cpuCores: number | null;
	memoryWorkingSetBytes: number | null;
}

export interface KubernetesWorkloadMetricsResponse {
	windowMinutes: number;
	bucketWidthSeconds: number;
	points: KubernetesWorkloadMetricsPoint[];
}

export interface KubernetesNamespaceSummary {
	namespace: string;
	/** Active/Terminating - null when k8s_cluster doesn't report it. */
	phase: string | null;
	podCount: number | null;
	cpuCores: number | null;
	memoryWorkingSetBytes: number | null;
	lastSeen: string;
}

export interface KubernetesNamespaceListResponse {
	windowMinutes: number;
	namespaces: KubernetesNamespaceSummary[];
	truncated: boolean;
}

/** Latest readings in the window (a fill level, not an average); used figures need both capacity and available. */
export interface KubernetesVolumeSummary {
	volumeName: string;
	namespace: string;
	podName: string;
	volumeType: string | null;
	claimName: string | null;
	capacityBytes: number | null;
	availableBytes: number | null;
	usedBytes: number | null;
	usedPercent: number | null;
	inodesUsedPercent: number | null;
	lastSeen: string;
}

export interface KubernetesVolumeListResponse {
	windowMinutes: number;
	volumes: KubernetesVolumeSummary[];
	truncated: boolean;
}

export interface KubernetesVolumeRef {
	namespace: string;
	podName: string;
	volumeName: string;
}

export interface KubernetesVolumeMetricsPoint {
	bucketStart: string;
	usedBytes: number | null;
	usedPercent: number | null;
	inodesUsedPercent: number | null;
}

export interface KubernetesVolumeMetricsResponse {
	windowMinutes: number;
	bucketWidthSeconds: number;
	points: KubernetesVolumeMetricsPoint[];
}

export interface KubernetesListFilter {
	search?: string;
	namespace?: string;
}

async function post(path: string, body: Uint8Array, signal?: AbortSignal): Promise<ArrayBuffer> {
	const res = await apiFetch(`${API_BASE_URL}${path}`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(body),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST ${path} failed: ${res.status} ${res.statusText}`);
	}
	return res.arrayBuffer();
}

export async function listKubernetesNodes(windowMinutes: number, filter: KubernetesNodeFilter, signal?: AbortSignal): Promise<KubernetesNodeListResponse> {
	const request = new GeneratedNodeListRequest();
	request.windowMinutes = windowMinutes;
	request.search = filter.search?.trim() || null;
	request.clusterName = filter.clusterName || null;

	const dto = GeneratedNodeListResponse.deserialize(await post('/api/kubernetes/nodes', GeneratedNodeListRequest.serialize(request), signal));
	if (dto == null) {
		throw new Error('Empty response body decoding KubernetesNodeListResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		truncated: dto.truncated,
		nodes: (dto.nodes ?? [])
			.filter((n) => n != null)
			.map((n) => ({
				nodeName: n.nodeName,
				clusterName: n.clusterName,
				ready: n.ready,
				cpuCores: n.cpuCores,
				cpuPercent: n.cpuPercent,
				memoryWorkingSetBytes: n.memoryWorkingSetBytes,
				memoryPercent: n.memoryPercent,
				podCount: n.podCount,
				lastSeen: n.lastSeen.toISOString()
			}))
	};
}

export async function getKubernetesNodeMetrics(nodeName: string, windowMinutes: number, signal?: AbortSignal): Promise<KubernetesNodeMetricsResponse> {
	const request = new GeneratedNodeMetricsRequest();
	request.nodeName = nodeName;
	request.windowMinutes = windowMinutes;

	const dto = GeneratedNodeMetricsResponse.deserialize(await post('/api/kubernetes/nodes/metrics', GeneratedNodeMetricsRequest.serialize(request), signal));
	if (dto == null) {
		throw new Error('Empty response body decoding KubernetesNodeMetricsResponse.');
	}
	return {
		nodeName: dto.nodeName,
		windowMinutes: dto.windowMinutes,
		bucketWidthSeconds: dto.bucketWidthSeconds,
		allocatableCpuCores: dto.allocatableCpuCores,
		points: (dto.points ?? [])
			.filter((p) => p != null)
			.map((p) => ({
				bucketStart: p.bucketStart.toISOString(),
				cpuCores: p.cpuCores,
				cpuPercent: p.cpuPercent,
				memoryWorkingSetBytes: p.memoryWorkingSetBytes,
				memoryPercent: p.memoryPercent
			}))
	};
}

export async function listKubernetesPods(windowMinutes: number, filter: KubernetesPodFilter, signal?: AbortSignal): Promise<KubernetesPodListResponse> {
	const request = new GeneratedPodListRequest();
	request.windowMinutes = windowMinutes;
	request.search = filter.search?.trim() || null;
	request.namespace = filter.namespace || null;
	request.nodeName = filter.nodeName || null;
	request.workloadKind = filter.workload?.kind ?? null;
	request.workloadName = filter.workload?.name ?? null;

	const dto = GeneratedPodListResponse.deserialize(await post('/api/kubernetes/pods', GeneratedPodListRequest.serialize(request), signal));
	if (dto == null) {
		throw new Error('Empty response body decoding KubernetesPodListResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		truncated: dto.truncated,
		pods: (dto.pods ?? [])
			.filter((p) => p != null)
			.map((p) => ({
				podName: p.podName,
				namespace: p.namespace,
				nodeName: p.nodeName,
				workloadKind: p.workloadKind,
				workloadName: p.workloadName,
				phase: p.phase,
				restarts: p.restarts,
				cpuCores: p.cpuCores,
				memoryWorkingSetBytes: p.memoryWorkingSetBytes,
				cpuLimitPercent: p.cpuLimitPercent,
				memoryLimitPercent: p.memoryLimitPercent,
				lastSeen: p.lastSeen.toISOString()
			}))
	};
}

function counts(c: KubernetesWorkloadCounts): KubernetesWorkloadCounts {
	return { desired: c.desired, ready: c.ready, current: c.current, misscheduled: c.misscheduled, active: c.active, succeeded: c.succeeded, failed: c.failed };
}

export async function listKubernetesWorkloads(
	kind: KubernetesWorkloadKind,
	windowMinutes: number,
	filter: KubernetesListFilter,
	signal?: AbortSignal
): Promise<KubernetesWorkloadListResponse> {
	const request = new GeneratedWorkloadListRequest();
	request.kind = kind;
	request.windowMinutes = windowMinutes;
	request.search = filter.search?.trim() || null;
	request.namespace = filter.namespace || null;

	const dto = GeneratedWorkloadListResponse.deserialize(await post('/api/kubernetes/workloads', GeneratedWorkloadListRequest.serialize(request), signal));
	if (dto == null) {
		throw new Error('Empty response body decoding KubernetesWorkloadListResponse.');
	}
	return {
		kind: dto.kind,
		windowMinutes: dto.windowMinutes,
		truncated: dto.truncated,
		workloads: (dto.workloads ?? [])
			.filter((w) => w != null)
			.map((w) => ({
				name: w.name,
				namespace: w.namespace,
				...counts(w),
				podCount: w.podCount,
				cpuCores: w.cpuCores,
				memoryWorkingSetBytes: w.memoryWorkingSetBytes,
				lastSeen: w.lastSeen.toISOString()
			}))
	};
}

export async function getKubernetesWorkloadMetrics(
	workload: KubernetesWorkloadRef,
	windowMinutes: number,
	signal?: AbortSignal
): Promise<KubernetesWorkloadMetricsResponse> {
	const request = new GeneratedWorkloadMetricsRequest();
	request.kind = workload.kind;
	request.namespace = workload.namespace;
	request.name = workload.name;
	request.windowMinutes = windowMinutes;

	const dto = GeneratedWorkloadMetricsResponse.deserialize(
		await post('/api/kubernetes/workloads/metrics', GeneratedWorkloadMetricsRequest.serialize(request), signal)
	);
	if (dto == null) {
		throw new Error('Empty response body decoding KubernetesWorkloadMetricsResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		bucketWidthSeconds: dto.bucketWidthSeconds,
		points: (dto.points ?? [])
			.filter((p) => p != null)
			.map((p) => ({
				bucketStart: p.bucketStart.toISOString(),
				...counts(p),
				cpuCores: p.cpuCores,
				memoryWorkingSetBytes: p.memoryWorkingSetBytes
			}))
	};
}

export async function listKubernetesNamespaces(windowMinutes: number, search: string, signal?: AbortSignal): Promise<KubernetesNamespaceListResponse> {
	const request = new GeneratedNamespaceListRequest();
	request.windowMinutes = windowMinutes;
	request.search = search.trim() || null;

	const dto = GeneratedNamespaceListResponse.deserialize(await post('/api/kubernetes/namespaces', GeneratedNamespaceListRequest.serialize(request), signal));
	if (dto == null) {
		throw new Error('Empty response body decoding KubernetesNamespaceListResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		truncated: dto.truncated,
		namespaces: (dto.namespaces ?? [])
			.filter((n) => n != null)
			.map((n) => ({
				namespace: n.namespace,
				phase: n.phase,
				podCount: n.podCount,
				cpuCores: n.cpuCores,
				memoryWorkingSetBytes: n.memoryWorkingSetBytes,
				lastSeen: n.lastSeen.toISOString()
			}))
	};
}

export async function listKubernetesVolumes(windowMinutes: number, filter: KubernetesListFilter, signal?: AbortSignal): Promise<KubernetesVolumeListResponse> {
	const request = new GeneratedVolumeListRequest();
	request.windowMinutes = windowMinutes;
	request.search = filter.search?.trim() || null;
	request.namespace = filter.namespace || null;

	const dto = GeneratedVolumeListResponse.deserialize(await post('/api/kubernetes/volumes', GeneratedVolumeListRequest.serialize(request), signal));
	if (dto == null) {
		throw new Error('Empty response body decoding KubernetesVolumeListResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		truncated: dto.truncated,
		volumes: (dto.volumes ?? [])
			.filter((v) => v != null)
			.map((v) => ({
				volumeName: v.volumeName,
				namespace: v.namespace,
				podName: v.podName,
				volumeType: v.volumeType,
				claimName: v.claimName,
				capacityBytes: v.capacityBytes,
				availableBytes: v.availableBytes,
				usedBytes: v.usedBytes,
				usedPercent: v.usedPercent,
				inodesUsedPercent: v.inodesUsedPercent,
				lastSeen: v.lastSeen.toISOString()
			}))
	};
}

export async function getKubernetesVolumeMetrics(volume: KubernetesVolumeRef, windowMinutes: number, signal?: AbortSignal): Promise<KubernetesVolumeMetricsResponse> {
	const request = new GeneratedVolumeMetricsRequest();
	request.namespace = volume.namespace;
	request.podName = volume.podName;
	request.volumeName = volume.volumeName;
	request.windowMinutes = windowMinutes;

	const dto = GeneratedVolumeMetricsResponse.deserialize(await post('/api/kubernetes/volumes/metrics', GeneratedVolumeMetricsRequest.serialize(request), signal));
	if (dto == null) {
		throw new Error('Empty response body decoding KubernetesVolumeMetricsResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		bucketWidthSeconds: dto.bucketWidthSeconds,
		points: (dto.points ?? [])
			.filter((p) => p != null)
			.map((p) => ({
				bucketStart: p.bucketStart.toISOString(),
				usedBytes: p.usedBytes,
				usedPercent: p.usedPercent,
				inodesUsedPercent: p.inodesUsedPercent
			}))
	};
}
