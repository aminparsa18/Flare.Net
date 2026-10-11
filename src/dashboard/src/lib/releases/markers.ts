// Deploy markers drawn on the Logs volume chart and the Metrics chart (ADR-0182 follow-up, ADR-0185).
// Pure placement helpers plus the loader; the SVG itself is components/releases/ReleaseMarkers.svelte.

import { listReleases, type Release } from '$lib/releases-api';

/** More than this in one window is noise, not signal: the newest are kept. */
export const MAX_RELEASE_MARKERS = 12;

export interface ReleaseMarker {
	release: Release;
	/** Deploy time, epoch ms. */
	time: number;
	/** Position along the chart, 0..1. */
	fraction: number;
}

/** Position of `time` between `from` and `to` (epoch ms), or null when it falls outside. */
export function fractionInRange(time: number, from: number, to: number): number | null {
	if (!(to > from) || time < from || time > to) return null;
	return (time - from) / (to - from);
}

/**
 * Position of `time` along a chart whose x axis is bucket *index* (not time): linear between the
 * two neighbouring buckets, so a gap in the data does not distort a marker. Null outside the
 * first..last bucket, or with fewer than two buckets.
 */
export function fractionInBuckets(time: number, bucketTimes: readonly number[]): number | null {
	const n = bucketTimes.length;
	if (n < 2 || time < bucketTimes[0] || time > bucketTimes[n - 1]) return null;
	let lo = 0;
	let hi = n - 1;
	while (hi - lo > 1) {
		const mid = (lo + hi) >> 1;
		if (bucketTimes[mid] <= time) lo = mid;
		else hi = mid;
	}
	const span = bucketTimes[hi] - bucketTimes[lo];
	const index = lo + (span > 0 ? (time - bucketTimes[lo]) / span : 0);
	return index / (n - 1);
}

/** Keeps the newest {@link MAX_RELEASE_MARKERS} releases that `place` can position. */
export function placeMarkers(releases: readonly Release[], place: (time: number) => number | null): ReleaseMarker[] {
	const placed: ReleaseMarker[] = [];
	for (const release of releases) {
		const time = new Date(release.deployedAt).getTime();
		const fraction = Number.isNaN(time) ? null : place(time);
		if (fraction !== null) placed.push({ release, time, fraction });
	}
	return placed.sort((a, b) => b.time - a.time).slice(0, MAX_RELEASE_MARKERS);
}

/**
 * Releases for the services a chart is filtered to (all of them when empty). A failure is an
 * empty list: markers are decoration and must never break the chart they sit on.
 */
export async function loadReleases(services: readonly string[], signal?: AbortSignal): Promise<Release[]> {
	try {
		if (services.length === 0) return await cachedList(undefined, signal);
		const lists = await Promise.all(services.map((s) => cachedList(s, signal)));
		return lists.flat();
	} catch {
		return [];
	}
}

// A dashboard draws many charts at once, most with the same services: share one request per service
// for a few seconds instead of one per panel. The scoped list also counts new errors (a span scan),
// which markers do not need, so they read the cheap unscoped list and filter it here.
const CACHE_TTL_MS = 30_000;
let cached: { at: number; promise: Promise<Release[]> } | null = null;

function cachedList(service: string | undefined, signal?: AbortSignal): Promise<Release[]> {
	if (!cached || Date.now() - cached.at > CACHE_TTL_MS) {
		const promise = listReleases(undefined);
		promise.catch(() => {
			if (cached?.promise === promise) cached = null;
		});
		cached = { at: Date.now(), promise };
	}
	return cached.promise.then((all) => {
		signal?.throwIfAborted();
		return service ? all.filter((r) => r.service === service) : all;
	});
}
