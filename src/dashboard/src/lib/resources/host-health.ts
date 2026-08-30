// "Host health" section: turns the raw HostStatsSnapshot numbers already on this panel
// into verdicts - same "answer the actual question, don't just expose the number"
// principle behind $lib/indexing/storage-health.ts, whose {tone, title, detail} shape and
// one-check-function-per-row structure this mirrors wholesale rather than inventing a new
// pattern.
//
// Docker/Kubernetes are each deliberately one row, not two (e.g. "Connected" only - no
// separate "daemon running"/"API server reachable" line): there's no signal in
// ResourceGraphSnapshot that distinguishes "transport reachable" from "the thing behind it
// responding" - both depend on the same single round trip each provider's poller already
// makes. Only one of the two rows is ever expected to actually appear at a time (see
// ResourceGraphSnapshot.provider's remarks - only one provider is ever configured per
// deploy), and each is omitted entirely (not shown as failing) when its provider isn't
// configured - it's opt-in and off by default, so a "not enabled" state isn't a health
// problem to flag, same reasoning the graph's own empty state on this page already uses.

import { WARNING_THRESHOLD_PERCENT } from './thresholds';
import { formatBytes } from '../ingestion/format';
import type { HostStatsSnapshot, ResourceGraphSnapshot } from '../api';

export type HostHealthTone = 'good' | 'warning';

export interface HostHealthCheck {
	id: string;
	tone: HostHealthTone;
	title: string;
	detail: string;
}

function checkCpu(snapshot: HostStatsSnapshot): HostHealthCheck {
	const warning = snapshot.cpuUsagePercent >= WARNING_THRESHOLD_PERCENT;
	return { id: 'cpu', tone: warning ? 'warning' : 'good', title: 'CPU', detail: warning ? 'High' : 'Normal' };
}

function checkMemory(snapshot: HostStatsSnapshot): HostHealthCheck {
	const percent = snapshot.memoryTotalBytes > 0 ? (100 * snapshot.memoryUsedBytes) / snapshot.memoryTotalBytes : 0;
	const warning = percent >= WARNING_THRESHOLD_PERCENT;
	return { id: 'memory', tone: warning ? 'warning' : 'good', title: 'Memory', detail: warning ? `${Math.round(percent)}%` : 'Normal' };
}

function checkDisk(snapshot: HostStatsSnapshot): HostHealthCheck {
	const percent = snapshot.diskTotalBytes > 0 ? (100 * snapshot.diskUsedBytes) / snapshot.diskTotalBytes : 0;
	const warning = percent >= WARNING_THRESHOLD_PERCENT;
	return { id: 'disk', tone: warning ? 'warning' : 'good', title: 'Disk', detail: warning ? `${Math.round(percent)}%` : 'Normal' };
}

// Swap total is often small/arbitrary (this dev machine's Docker Desktop VM: ~1GB), so
// "% of swap used" doesn't mean much the way it does for memory/disk - *any* non-trivial
// swap use is itself the signal (the kernel is under enough memory pressure to page out
// at all), hence an absolute floor rather than a percentage. 256MB is a stated, tunable
// number, not a percent-of-an-arbitrary-total.
const SWAP_WARNING_BYTES = 256 * 1024 * 1024;

function checkSwap(snapshot: HostStatsSnapshot): HostHealthCheck {
	const warning = snapshot.swapUsedBytes >= SWAP_WARNING_BYTES;
	return { id: 'swap', tone: warning ? 'warning' : 'good', title: 'Swap', detail: warning ? formatBytes(snapshot.swapUsedBytes) : 'Normal' };
}

function checkDocker(resourceSnapshot: ResourceGraphSnapshot | null): HostHealthCheck | null {
	if (!resourceSnapshot?.available || resourceSnapshot.provider !== 'Docker') {
		return null; // not configured, or the live provider is Kubernetes instead - see the file header remarks.
	}
	if (resourceSnapshot.unavailableReason) {
		return { id: 'docker', tone: 'warning', title: 'Docker', detail: resourceSnapshot.unavailableReason };
	}
	return { id: 'docker', tone: 'good', title: 'Docker', detail: 'Connected' };
}

function checkKubernetes(resourceSnapshot: ResourceGraphSnapshot | null): HostHealthCheck | null {
	if (!resourceSnapshot?.available || resourceSnapshot.provider !== 'Kubernetes') {
		return null; // not configured, or the live provider is Docker instead - see the file header remarks.
	}
	if (resourceSnapshot.unavailableReason) {
		return { id: 'kubernetes', tone: 'warning', title: 'Kubernetes', detail: resourceSnapshot.unavailableReason };
	}
	return { id: 'kubernetes', tone: 'good', title: 'Kubernetes', detail: 'Connected' };
}

export function computeHostHealth(snapshot: HostStatsSnapshot, resourceSnapshot: ResourceGraphSnapshot | null): HostHealthCheck[] {
	return [
		checkCpu(snapshot),
		checkMemory(snapshot),
		checkDisk(snapshot),
		checkSwap(snapshot),
		checkDocker(resourceSnapshot),
		checkKubernetes(resourceSnapshot)
	].filter((check): check is HostHealthCheck => check !== null);
}
