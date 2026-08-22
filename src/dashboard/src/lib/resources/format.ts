// Host overview panel's own formatting helpers - `formatBytes` in $lib/ingestion/format.ts
// formats one value at a time, which isn't quite right for a "used / total" pair: two
// independently-scaled values can land in different units ("800 MB / 16 GB"). This scales
// both sides to whichever unit fits the *total*, so they always share one.

import { formatBytes, formatCount } from '$lib/ingestion/format';

const BYTE_UNITS = ['B', 'KB', 'MB', 'GB', 'TB'] as const;

/** `"6.2 / 16 GB"`. Falls back to independently-formatted values when `totalBytes` is non-positive (nothing sensible to scale against). */
export function formatByteUsage(usedBytes: number, totalBytes: number): string {
	if (totalBytes <= 0) {
		return `${formatBytes(usedBytes)} / ${formatBytes(totalBytes)}`;
	}

	const exponent = Math.min(BYTE_UNITS.length - 1, Math.floor(Math.log(totalBytes) / Math.log(1024)));
	const scale = 1024 ** exponent;
	const totalScaled = totalBytes / scale;
	const usedScaled = usedBytes / scale;
	const decimals = exponent === 0 ? 0 : totalScaled < 10 ? 1 : 0;
	return `${usedScaled.toFixed(decimals)} / ${totalScaled.toFixed(decimals)} ${BYTE_UNITS[exponent]}`;
}

/** `"4d 17h"` - drops to `"Xh Ym"` under a day and `"Xm"` under an hour, so a freshly-started host doesn't just show "0d 0h". */
export function formatUptime(totalSeconds: number): string {
	const totalMinutes = Math.floor(totalSeconds / 60);
	const days = Math.floor(totalMinutes / 1440);
	const hours = Math.floor((totalMinutes % 1440) / 60);
	const minutes = totalMinutes % 60;

	if (days > 0) return `${days}d ${hours}h`;
	if (hours > 0) return `${hours}h ${minutes}m`;
	return `${minutes}m`;
}

/** `"2.4 MB/s"` - network/disk throughput, always non-negative from the backend (see HostStatsSnapshot.cs's remarks on the fields this formats). */
export function formatByteRate(bytesPerSecond: number): string {
	return `${formatBytes(bytesPerSecond)}/s`;
}

/** `"+1.2 GB/day"` / `"-340 MB/day"` - unlike the rate above, disk usage can shrink (data pruned/rotated), so this is sign-aware rather than assuming non-negative input the way `formatBytes` does. */
export function formatByteGrowthPerDay(bytesPerDay: number): string {
	const sign = bytesPerDay < 0 ? '-' : '+';
	return `${sign}${formatBytes(Math.abs(bytesPerDay))}/day`;
}

/** `"340 pkt/s"` - same compact-count formatting `formatCount` already gives event counts elsewhere in the dashboard. */
export function formatPacketRate(packetsPerSecond: number): string {
	return `${formatCount(packetsPerSecond)} pkt/s`;
}
