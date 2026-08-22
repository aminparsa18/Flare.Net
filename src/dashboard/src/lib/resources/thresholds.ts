// Shared severity thresholds for the Host overview panel - one place these numbers are
// written down, so HostOverview.svelte's tile coloring (severityTextClass/meterClasses)
// and host-health.ts's checks always agree on what "high" means. Same 90%/75% split
// $lib/components/indexing/IndexingSummaryTiles.svelte's disk-usage card originally
// established for this dashboard.

export const WARNING_THRESHOLD_PERCENT = 75;
export const CRITICAL_THRESHOLD_PERCENT = 90;
