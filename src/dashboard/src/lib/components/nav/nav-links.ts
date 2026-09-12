// Single source of truth for the app's top-level page links + the Admin-only /auth
// gating - extracted out of AppNav.svelte so CommandPalette.svelte's "Navigate" group
// can reuse the exact same list (including the gating) instead of re-deriving it.

import type { AuthState } from '$lib/auth/state.svelte';
import * as m from '$lib/paraglide/messages';

export interface NavLink {
	href: string;
	label: string;
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
		{ href: '/metrics', label: m.nav_metrics() },
		{ href: '/ingestion', label: m.nav_ingestion() },
		{ href: '/indexing', label: m.nav_indexing() },
		{ href: '/alerts', label: m.nav_alerts() },
		{ href: '/resources', label: m.nav_resources() },
		{ href: '/dashboards', label: m.nav_dashboards() },
		{ href: '/views', label: m.nav_views() },
		...(!auth.authEnabled || auth.currentUser?.role === 'Admin' ? [{ href: '/auth', label: m.nav_auth() }] : [])
	];
}
