// Display helpers shared by the Kubernetes page's tables and drill-down sheets.

import { formatAutoScaled } from '$lib/metrics/axis';
import type { BadgeVariant } from '$lib/components/ui/badge';
import type { KubernetesWorkloadCounts, KubernetesWorkloadKind } from '$lib/kubernetes-api';

/** CPU in Kubernetes' own notation: millicores under one core ("250m"), cores above ("1.50"). */
export function formatCores(cores: number): string {
	return cores < 1 ? `${Math.round(cores * 1000)}m` : cores.toFixed(2);
}

export function formatBytes(bytes: number): string {
	return formatAutoScaled(bytes, 'By');
}

/** Failed/Unknown read as trouble, Pending as in-progress, Running/Succeeded as normal. */
export function phaseVariant(phase: string): BadgeVariant {
	switch (phase) {
		case 'Failed':
		case 'Unknown':
			return 'destructive';
		case 'Pending':
			return 'warning';
		default:
			return 'outline';
	}
}

/** Kinds whose status is "ready of desired" replicas (DaemonSet: nodes) - the rest are run-to-completion kinds. */
export function isReplicatedKind(kind: KubernetesWorkloadKind): boolean {
	return kind === 'Deployment' || kind === 'StatefulSet' || kind === 'DaemonSet';
}

/**
 * The Workloads table's status sort key - higher is worse: replicas short of desired for the
 * replicated kinds, failed pods for a Job, active jobs for a CronJob (the one figure it has).
 * Null when the counts it needs aren't reported.
 */
export function workloadStatusRank(kind: KubernetesWorkloadKind, counts: KubernetesWorkloadCounts): number | null {
	switch (kind) {
		case 'Job':
			return counts.failed;
		case 'CronJob':
			return counts.active;
		default:
			return counts.desired != null && counts.ready != null ? counts.desired - counts.ready : null;
	}
}

/** Ready vs desired: all ready reads normal, some short as in-progress, none of a nonzero desire as trouble. */
export function readyVariant(ready: number, desired: number): BadgeVariant {
	if (ready >= desired) return 'outline';
	return ready === 0 ? 'destructive' : 'warning';
}
