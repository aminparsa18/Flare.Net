// "Last seen" labels for the Kubernetes tables - same thresholds and wording as
// HostsTable.svelte's (the hostsPage_* strings are reused rather than duplicated).

import * as m from '$lib/paraglide/messages';

/** Several missed scrapes at the receivers' usual intervals - the object has most likely stopped reporting (or, for a pod, been deleted). */
export const STALE_AFTER_MS = 5 * 60_000;

export function formatAgo(iso: string, now: number): string {
	const minutes = Math.floor((now - new Date(iso).getTime()) / 60_000);
	if (minutes < 1) return m.hostsPage_justNow();
	if (minutes < 60) return m.hostsPage_minutesAgo({ minutes });
	return m.hostsPage_hoursAgo({ hours: Math.floor(minutes / 60) });
}

export function isStale(iso: string, now: number): boolean {
	return now - new Date(iso).getTime() > STALE_AFTER_MS;
}
