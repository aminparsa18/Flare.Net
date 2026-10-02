import * as m from '$lib/paraglide/messages';
import type { AlertSeverity } from '$lib/alerts-api';

/** Localized name of an alert rule's severity. */
export function severityLabel(severity: AlertSeverity): string {
	switch (severity) {
		case 'Error':
			return m.alertSeverity_error();
		case 'Warning':
			return m.alertSeverity_warning();
		case 'Info':
			return m.alertSeverity_info();
		default:
			return m.alertSeverity_critical();
	}
}

/** Badge variant for a severity: Critical/Error read as destructive, the rest stay muted. */
export function severityBadgeVariant(severity: AlertSeverity): 'destructive' | 'secondary' | 'outline' {
	return severity === 'Critical' || severity === 'Error' ? 'destructive' : severity === 'Warning' ? 'secondary' : 'outline';
}
