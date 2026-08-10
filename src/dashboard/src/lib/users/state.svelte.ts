// Central reactive state for the Users page - mirrors AlertsState's shape
// ($lib/alerts/state.svelte.ts): a class with $state fields, provided via usersContext
// (context.ts) rather than passed as props, per this repo's svelte-best-practices skill.

import { listUsers, setUserRole, setUserDisabled, type UserSummary } from '$lib/users-api';
import type { UserRole } from '$lib/auth-api';

export class UsersState {
	users = $state.raw<UserSummary[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** Id of the row currently mid-mutation (role change or disable toggle) - disables
	 * that row's own controls without blocking the rest of the table, same "just the
	 * affected row" scoping AlertRuleTable's per-rule testResults uses. */
	savingId = $state<string | null>(null);
	saveError = $state<string | null>(null);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.users = await listUsers();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	async changeRole(user: UserSummary, role: UserRole): Promise<void> {
		if (role === user.role) return;
		this.savingId = user.id;
		this.saveError = null;
		try {
			const updated = await setUserRole(user.id, role);
			this.users = this.users.map((u) => (u.id === updated.id ? updated : u));
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.savingId = null;
		}
	}

	async toggleDisabled(user: UserSummary, isDisabled: boolean): Promise<void> {
		this.savingId = user.id;
		this.saveError = null;
		try {
			const updated = await setUserDisabled(user.id, isDisabled);
			this.users = this.users.map((u) => (u.id === updated.id ? updated : u));
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.savingId = null;
		}
	}
}
