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
import * as m from '$lib/paraglide/messages';

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
			return m.traceStatus_ok();
		case 'STATUS_CODE_ERROR':
			return m.traceStatus_error();
		default:
			return m.traceStatus_unset();
	}
}

/**
 * The status code to badge a trace-list row with: `span.statusCode` on its own only
 * reflects the *root* span (that's all a `SpanFilter.rootSpansOnly` row carries), so a
 * trace whose root span succeeded (e.g. a gateway returning 200) but has an erroring span
 * deeper in the call chain would otherwise badge as healthy until the waterfall is
 * opened. `hasError` is the server-computed rollup across every span in the trace (see
 * `SpanDto.hasError`'s C# remarks) - when true and the root itself didn't already fail,
 * this reports `STATUS_CODE_ERROR` so `statusVariant`/`statusLabel` render the same
 * "Error" badge they would for a directly-failing root.
 */
export function rolledUpStatusCode(span: SpanDto): string {
	if (span.hasError && span.statusCode !== 'STATUS_CODE_ERROR') {
		return 'STATUS_CODE_ERROR';
	}
	return span.statusCode;
}

/** OTel SpanKind (Span.proto's Span.SpanKind enum) - 0 through 5, spec-fixed. */
function kindLabelFor(kind: number): string | null {
	switch (kind) {
		case 0:
			return m.traceKind_unspecified();
		case 1:
			return m.traceKind_internal();
		case 2:
			return m.traceKind_server();
		case 3:
			return m.traceKind_client();
		case 4:
			return m.traceKind_producer();
		case 5:
			return m.traceKind_consumer();
		default:
			return null;
	}
}

export function kindLabel(kind: number): string {
	return kindLabelFor(kind) ?? m.traceKind_fallback({ kind });
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
