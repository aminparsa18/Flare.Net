// Single source of truth for span Status/Kind display - same "one place, badge +
// waterfall both read from it" spirit as `logs/severity.ts` does for SeverityNumber.

import type { BadgeVariant } from '$lib/components/ui/badge';
import type { SpanDto } from '$lib/traces-api';
import type { LucideIcon } from '@lucide/svelte';
import GlobeIcon from '@lucide/svelte/icons/globe';
import Settings2Icon from '@lucide/svelte/icons/settings-2';
import ArrowRightIcon from '@lucide/svelte/icons/arrow-right';
import DatabaseIcon from '@lucide/svelte/icons/database';
import SendIcon from '@lucide/svelte/icons/send';
import InboxIcon from '@lucide/svelte/icons/inbox';
import CircleHelpIcon from '@lucide/svelte/icons/circle-help';

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

/**
 * OTel SpanKind as an icon - "another opportunity to make the waterfall immediately
 * understandable" without reading every row's name/service pair first. Takes the whole
 * span, not just `kind`, because CLIENT additionally special-cases the `db.system`
 * semantic-convention attribute (a real OTel key, not a Flare invention - see
 * ExampleApp.LogGenerator's EmitWaterfall remarks) to a database icon rather than a
 * generic outbound-call arrow, when present.
 */
export function kindIcon(span: SpanDto): LucideIcon {
	switch (span.kind) {
		case 1: // Internal
			return Settings2Icon;
		case 2: // Server
			return GlobeIcon;
		case 3: // Client
			return span.spanAttributes['db.system'] ? DatabaseIcon : ArrowRightIcon;
		case 4: // Producer
			return SendIcon;
		case 5: // Consumer
			return InboxIcon;
		default: // Unspecified
			return CircleHelpIcon;
	}
}
