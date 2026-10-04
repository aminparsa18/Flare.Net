// Browser notifications for fired alerts. While enabled (Settings > Notifications) and
// permission is granted, polls /api/alerts/states and raises a Notification when a rule's
// lastFiredAt advances. The first poll only records a baseline, so opening the app never
// replays old fires. Best-effort: a failed poll is skipped, nothing is queued.
import { listAlertRuleStatuses, listAlertRules } from '$lib/alerts-api';
import * as m from '$lib/paraglide/messages';

const POLL_MS = 30_000;

export function browserNotificationsSupported(): boolean {
	return typeof Notification !== 'undefined';
}

/** Starts polling; returns a stop function. */
export function startAlertWatcher(): () => void {
	const lastFired = new Map<string, string>();
	const names = new Map<string, string>();
	let baselined = false;
	let stopped = false;

	async function tick() {
		if (document.visibilityState === 'hidden' && baselined) return;
		try {
			const statuses = await listAlertRuleStatuses();
			const fresh = statuses.filter((s) => s.lastFiredAt && lastFired.get(s.ruleId) !== s.lastFiredAt);
			if (baselined && fresh.some((s) => !names.has(s.ruleId))) {
				for (const r of (await listAlertRules()).rules) names.set(r.id, r.name);
			}
			for (const s of fresh) {
				lastFired.set(s.ruleId, s.lastFiredAt!);
				if (!baselined || !s.firing || stopped) continue;
				new Notification(m.settingsNotifications_browserTitle(), {
					body: names.get(s.ruleId) ?? s.ruleId,
					tag: `flare-alert-${s.ruleId}`
				});
			}
			baselined = true;
		} catch {
			// Unreachable API - try again next tick.
		}
	}

	void tick();
	const timer = setInterval(() => void tick(), POLL_MS);
	return () => {
		stopped = true;
		clearInterval(timer);
	};
}
