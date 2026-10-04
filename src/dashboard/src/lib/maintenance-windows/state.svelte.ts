// Central reactive state for the alerts page's Maintenance tab - same shape as
// NotificationChannelsState (`$lib/notification-channels/state.svelte.ts`), minus send-test.

import {
	listMaintenanceWindows,
	createMaintenanceWindow,
	updateMaintenanceWindow,
	deleteMaintenanceWindow,
	type MaintenanceWindow,
	type MaintenanceWindowRequest
} from '$lib/maintenance-windows-api';
import type { AlertRule } from '$lib/alerts-api';
import { labelsMatch } from '$lib/alerts/labels';

export class MaintenanceWindowsState {
	windows = $state.raw<MaintenanceWindow[]>([]);
	/** Ids of windows active right now, as of the last `load()`. */
	activeWindowIds = $state.raw<string[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** `null` closed, `'new'` creating, a `MaintenanceWindow` editing it. */
	formTarget = $state<MaintenanceWindow | 'new' | null>(null);
	saving = $state(false);
	saveError = $state<string | null>(null);

	/** True when an active window silences `rule` - drives the rules table's "muted" badge. Mirrors the server's `MaintenanceWindowSchedule.Covers`: a window with neither rule ids nor label matchers covers everything, otherwise a listed id OR matching labels. */
	isRuleMuted(rule: Pick<AlertRule, 'id' | 'labels'>): boolean {
		return this.activeWindowsFor(rule).length > 0;
	}

	/** Active windows covering `rule`, same coverage rule as `isRuleMuted`. */
	activeWindowsFor(rule: Pick<AlertRule, 'id' | 'labels'>): MaintenanceWindow[] {
		return this.windows.filter(
			(w) =>
				this.activeWindowIds.includes(w.id) &&
				((w.ruleIds.length === 0 && Object.keys(w.labelMatchers).length === 0) || w.ruleIds.includes(rule.id) || labelsMatch(w.labelMatchers, rule.labels))
		);
	}

	/** The active one-off window created by the row's Mute action (scoped to exactly this rule, no label matchers) - the only kind the row's Unmute may end. */
	activeQuickMute(rule: Pick<AlertRule, 'id' | 'labels'>): MaintenanceWindow | null {
		return (
			this.activeWindowsFor(rule).find(
				(w) => w.recurrence === 'None' && w.ruleIds.length === 1 && w.ruleIds[0] === rule.id && Object.keys(w.labelMatchers).length === 0
			) ?? null
		);
	}

	/** When the rule's mute ends: the latest end among its active one-off windows, or null if a recurring window also covers it (its next gap isn't derivable client-side) or nothing does. */
	muteEndsAt(rule: Pick<AlertRule, 'id' | 'labels'>): string | null {
		const active = this.activeWindowsFor(rule);
		if (active.length === 0 || active.some((w) => w.recurrence !== 'None')) return null;
		return active.map((w) => w.endsAt).reduce((a, b) => (new Date(a) > new Date(b) ? a : b));
	}

	/** Mutes `rule` until `endsAt` with a one-off window scoped to it (no new storage - ADR-0055). */
	async mute(rule: Pick<AlertRule, 'id' | 'name'>, endsAt: Date, reason: string): Promise<void> {
		this.error = null;
		try {
			await createMaintenanceWindow({
				name: `Muted: ${rule.name}`.slice(0, 200),
				description: reason.trim(),
				ruleIds: [rule.id],
				startsAt: new Date().toISOString(),
				endsAt: endsAt.toISOString(),
				recurrence: 'None',
				daysOfWeek: [],
				repeatUntil: null,
				timeZone: 'UTC',
				labelMatchers: {}
			});
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	/** Ends a quick-mute window early. A window can't end at or before its start, so one created within the last second is deleted instead. */
	async unmute(window: MaintenanceWindow): Promise<void> {
		const now = Date.now();
		if (now - new Date(window.startsAt).getTime() < 1000) {
			await this.remove(window.id);
			return;
		}

		try {
			await updateMaintenanceWindow(window.id, {
				name: window.name,
				description: window.description,
				ruleIds: window.ruleIds,
				startsAt: window.startsAt,
				endsAt: new Date(now).toISOString(),
				recurrence: window.recurrence,
				daysOfWeek: window.daysOfWeek,
				repeatUntil: window.repeatUntil,
				timeZone: window.timeZone,
				labelMatchers: window.labelMatchers
			});
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			const res = await listMaintenanceWindows();
			this.windows = res.windows;
			this.activeWindowIds = res.activeWindowIds;
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	openCreate(): void {
		this.saveError = null;
		this.formTarget = 'new';
	}

	openEdit(window: MaintenanceWindow): void {
		this.saveError = null;
		this.formTarget = window;
	}

	closeForm(): void {
		this.formTarget = null;
	}

	async save(request: MaintenanceWindowRequest): Promise<void> {
		const target = this.formTarget;
		this.saving = true;
		this.saveError = null;
		try {
			if (target && target !== 'new') {
				await updateMaintenanceWindow(target.id, request);
			} else {
				await createMaintenanceWindow(request);
			}
			this.formTarget = null;
			await this.load();
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.saving = false;
		}
	}

	async remove(id: string): Promise<void> {
		try {
			await deleteMaintenanceWindow(id);
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}
}
