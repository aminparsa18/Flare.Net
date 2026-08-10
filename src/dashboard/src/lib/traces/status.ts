// Single source of truth for span Status/Kind display - same "one place, badge +
// waterfall both read from it" spirit as `logs/severity.ts` does for SeverityNumber.

import type { BadgeVariant } from '$lib/components/ui/badge';

export function statusVariant(statusCode: string): BadgeVariant {
	switch (statusCode) {
		case 'STATUS_CODE_ERROR':
			return 'destructive';
		case 'STATUS_CODE_OK':
			return 'secondary';
		default:
			return 'outline'; // STATUS_CODE_UNSET
	}
}

export function statusLabel(statusCode: string): string {
	switch (statusCode) {
		case 'STATUS_CODE_OK':
			return 'OK';
		case 'STATUS_CODE_ERROR':
			return 'Error';
		default:
			return 'Unset';
	}
}

/** OTel SpanKind (Span.proto's Span.SpanKind enum) - 0 through 5, spec-fixed. */
const KIND_LABELS: Record<number, string> = {
	0: 'Unspecified',
	1: 'Internal',
	2: 'Server',
	3: 'Client',
	4: 'Producer',
	5: 'Consumer'
};

export function kindLabel(kind: number): string {
	return KIND_LABELS[kind] ?? `Kind ${kind}`;
}
