// Central reactive state for the Alerts page - mirrors LogsExplorerState's shape
// (`$lib/logs/state.svelte.ts`): a class with `$state` fields, provided via
// `alertsContext` (context.ts) rather than passed as props, per this repo's
// svelte-best-practices skill.

import {
	listAlertRules,
	createAlertRule,
	updateAlertRule,
	deleteAlertRule,
	getAlertHistory,
	type AlertRule,
	type AlertRuleRequest,
	type AlertHistoryEntry
} from '$lib/alerts-api';
import type { AlertPanelDraft } from '$lib/deep-links';

export class AlertsState {
	rules = $state.raw<AlertRule[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/**
	 * Drives the create/edit dialog - `null` closed, `'new'` creating, an `AlertRule`
	 * editing that rule. Mirrors `EventDetailSheet`'s `open={x !== null}` pattern rather
	 * than a separate boolean + separate "which rule" field.
	 */
	formTarget = $state<AlertRule | 'new' | null>(null);
	saving = $state(false);
	saveError = $state<string | null>(null);

	/**
	 * A pending "Create alert from panel" draft (see `$lib/deep-links.ts`'s
	 * `AlertPanelDraft`/`parseAlertDeepLinkParams`) - deliberately *not* `$state`. It's a
	 * one-shot handoff to AlertRuleFormDialog's own reset effect (consumed, then nulled
	 * out, the first time `formTarget` becomes `'new'`); if this were reactive state, that
	 * same nulling would re-trigger the effect and wipe the fields it just set. Plain
	 * `openCreate()` (the toolbar's own "+ New alert" button) always clears this first, so
	 * a stale draft never leaks into an unrelated blank rule.
	 */
	createDraft: AlertPanelDraft | null = null;

	/** Drives the history sheet - the rule currently being inspected, or null if closed. */
	historyRule = $state<AlertRule | null>(null);
	history = $state.raw<AlertHistoryEntry[]>([]);
	historyLoading = $state(false);
	historyError = $state<string | null>(null);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			const res = await listAlertRules();
			this.rules = res.rules;
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	openCreate(): void {
		this.saveError = null;
		this.createDraft = null;
		this.formTarget = 'new';
	}

	/** Opens the create dialog pre-filled from a Logs/Metrics dashboard panel - see
	 *  `createDraft`'s own remarks and `DashboardPanelCard.svelte`'s "Create alert" action,
	 *  the only caller. */
	openCreateFromDraft(draft: AlertPanelDraft): void {
		this.saveError = null;
		this.createDraft = draft;
		this.formTarget = 'new';
	}

	openEdit(rule: AlertRule): void {
		this.saveError = null;
		this.formTarget = rule;
	}

	closeForm(): void {
		this.formTarget = null;
	}

	async create(request: AlertRuleRequest): Promise<void> {
		this.saving = true;
		this.saveError = null;
		try {
			await createAlertRule(request);
			this.formTarget = null;
			await this.load();
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.saving = false;
		}
	}

	async update(id: string, request: AlertRuleRequest): Promise<void> {
		this.saving = true;
		this.saveError = null;
		try {
			await updateAlertRule(id, request);
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
			await deleteAlertRule(id);
			if (this.historyRule?.id === id) this.historyRule = null;
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	openHistory(rule: AlertRule): void {
		this.historyRule = rule;
		void this.loadHistory();
	}

	closeHistory(): void {
		this.historyRule = null;
	}

	async loadHistory(): Promise<void> {
		const rule = this.historyRule;
		if (!rule) return;
		this.historyLoading = true;
		this.historyError = null;
		try {
			const res = await getAlertHistory(rule.id);
			// Guard against a stale response landing after the sheet moved on to a
			// different (or no) rule - same "abort/ignore late responses" concern
			// runSearch handles with an AbortController, done here with a plain check
			// since history fetches are infrequent/cheap enough not to need one.
			if (this.historyRule?.id === rule.id) this.history = res.events;
		} catch (err) {
			if (this.historyRule?.id === rule.id) {
				this.historyError = err instanceof Error ? err.message : String(err);
			}
		} finally {
			if (this.historyRule?.id === rule.id) this.historyLoading = false;
		}
	}
}
