// Single source of truth for the app's top-level page links + the Admin-only /auth
// gating - extracted out of AppNav.svelte so CommandPalette.svelte's "Navigate" group
// can reuse the exact same list (including the gating) instead of re-deriving it.

import type { AuthState } from '$lib/auth/state.svelte';
import * as m from '$lib/paraglide/messages';

export interface NavLink {
	href: string;
	label: string;
	/** Shown in NavUserMenu's "more" dropdown instead of AppNav's top bar - the bar was
	 *  outgrowing its width. CommandPalette still lists every link either way. */
	inMenu?: boolean;
}

/** /auth (the consolidated enable-auth/configure-methods/manage-users screen) is
 *  Admin-only - except while auth is off entirely, when everyone has full access and
 *  needs a way to actually find where to turn it on. See AppNav.svelte's own history
 *  for the full reasoning.
 *
 *  Labels are m.*() calls, not static strings - navLinks() already recomputes on every
 *  call (AppNav.svelte's `links = $derived(navLinks(auth))`), so this picks up the
 *  current locale for free, no extra reactivity plumbing needed. */
export function navLinks(auth: AuthState): NavLink[] {
	return [
		{ href: '/', label: m.nav_logs() },
		{ href: '/traces', label: m.nav_traces() },
		{ href: '/errors', label: m.nav_errors() },
		{ href: '/frontend', label: m.nav_frontend(), inMenu: true },
		{ href: '/messaging', label: m.nav_messaging() },
		{ href: '/external-apis', label: m.nav_externalApis(), inMenu: true },
		{ href: '/llm', label: m.nav_llm(), inMenu: true },
		{ href: '/profiles', label: m.nav_profiles(), inMenu: true },
		{ href: '/metrics', label: m.nav_metrics() },
		{ href: '/ingestion', label: m.nav_ingestion() },
		{ href: '/alerts', label: m.nav_alerts() },
		{ href: '/resources', label: m.nav_resources() },
		{ href: '/hosts', label: m.nav_hosts(), inMenu: true },
		{ href: '/kubernetes', label: m.nav_kubernetes(), inMenu: true },
		{ href: '/dashboards', label: m.nav_dashboards() },
		{ href: '/views', label: m.nav_views() },
		...(!auth.authEnabled || auth.currentUser?.role === 'Admin' ? [{ href: '/auth', label: m.nav_auth() }] : [])
	];
}

/** Settings sections that used to be top-level pages (/settings/slos, /settings/pipeline-rules,
 *  /settings/ingest-keys, /settings/access-tokens). Shared by the settings rail and the command
 *  palette. Personal access tokens need a signed-in identity to own one. */
export function settingsManagementLinks(auth: AuthState): NavLink[] {
	return [
		{ href: '/settings/slos', label: m.nav_slos() },
		{ href: '/settings/pipeline-rules', label: m.nav_pipelineRules() },
		{ href: '/settings/log-metrics', label: m.nav_logMetrics() },
		...(auth.authEnabled ? [{ href: '/settings/access-tokens', label: m.accessTokensPage_heading() }] : [])
	];
}

/** Instance-level admin pages (Settings > Workspace): notification channels, maintenance
 *  windows, audit log, indexing, projects, ingest keys. Same Admin gate as /auth, including "everyone
 *  while auth is off". Shared by the settings rail and the command palette. */
export function workspaceLinks(auth: AuthState): NavLink[] {
	if (auth.authEnabled && auth.currentUser?.role !== 'Admin') return [];
	return [
		{ href: '/settings/channels', label: m.notificationChannelTable_heading() },
		{ href: '/settings/maintenance-windows', label: m.maintenanceWindowTable_heading() },
		{ href: '/settings/alert-templates', label: m.alertTemplates_heading() },
		{ href: '/settings/oncall-rotations', label: m.oncall_heading() },
		{ href: '/settings/synthetic-monitors', label: m.synthetic_heading() },
		{ href: '/settings/audit-log', label: m.auditLogPage_heading() },
		{ href: '/settings/indexing', label: m.nav_indexing() },
		{ href: '/settings/usage', label: m.nav_usage() },
		{ href: '/settings/retention', label: m.nav_retention() },
		{ href: '/settings/telemetry-export', label: m.telemetryExport_navLabel() },
		{ href: '/settings/projects', label: m.projectsPage_heading() },
		{ href: '/settings/ingest-keys', label: m.ingestKeysPage_heading() },
		{ href: '/settings/source-maps', label: m.sourceMapsPage_heading() }
	];
}
