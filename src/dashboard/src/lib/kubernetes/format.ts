// Display helpers shared by the Kubernetes page's tables and drill-down sheets.

import { formatAutoScaled } from '$lib/metrics/axis';
import type { BadgeVariant } from '$lib/components/ui/badge';

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
