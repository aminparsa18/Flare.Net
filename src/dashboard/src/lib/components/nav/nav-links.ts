// Single source of truth for the app's top-level page links + the Admin-only /auth
// gating - extracted out of AppNav.svelte so CommandPalette.svelte's "Navigate" group
// can reuse the exact same list (including the gating) instead of re-deriving it.

import type { AuthState } from '$lib/auth/state.svelte';

export interface NavLink {
	href: string;
	label: string;
}

/** /auth (the consolidated enable-auth/configure-methods/manage-users screen) is
 *  Admin-only - except while auth is off entirely, when everyone has full access and
 *  needs a way to actually find where to turn it on. See AppNav.svelte's own history
 *  for the full reasoning. */
export function navLinks(auth: AuthState): NavLink[] {
	return [
		{ href: '/', label: 'Logs' },
		{ href: '/traces', label: 'Traces' },
		{ href: '/metrics', label: 'Metrics' },
		{ href: '/ingestion', label: 'Ingestion' },
		{ href: '/indexing', label: 'Indexing' },
		{ href: '/alerts', label: 'Alerts' },
		{ href: '/resources', label: 'Resources' },
		{ href: '/views', label: 'Views' },
		...(!auth.authEnabled || auth.currentUser?.role === 'Admin' ? [{ href: '/auth', label: 'Auth' }] : [])
	];
}
