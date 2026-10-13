// Reactive state for the /sessions/feedback inbox - messages users sent from the app (ADR-0193). On demand, with a
// manual refresh, like the sessions table.

import { getAppFeedback, type AppFeedback } from '#lib/app-sessions-api.ts';
import { SERVICES_WINDOW_PRESETS, type ServicesWindowPreset } from '#lib/services/state.svelte.ts';

export const APP_FEEDBACK_WINDOW_PRESETS = SERVICES_WINDOW_PRESETS;

/** How far before a message its session may have started; the timeline link needs a window that contains the whole session. */
export const FEEDBACK_SESSION_LOOKBACK_MS = 24 * 60 * 60 * 1000;

export class AppFeedbackState {
	windowPreset = $state<ServicesWindowPreset>('24h');
	/** '' = all. */
	service = $state('');
	items = $state.raw<AppFeedback[] | null>(null);
	truncated = $state(false);
	/** The service picker's options: every service in the loaded messages, kept across filtering. */
	services = $state.raw<string[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	#abort: AbortController | null = null;

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		this.loading = true;
		this.error = null;
		try {
			const minutes = APP_FEEDBACK_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 1440;
			const response = await getAppFeedback({ windowMinutes: minutes, service: this.service }, abort.signal);
			if (abort.signal.aborted) return;
			this.items = response.items;
			this.truncated = response.truncated;
			if (!this.service) this.services = [...new Set(response.items.map((i) => i.serviceName))].sort();
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (this.#abort === abort) this.loading = false;
		}
	}

	setWindowPreset(preset: ServicesWindowPreset): void {
		this.windowPreset = preset;
		void this.load();
	}

	setService(service: string): void {
		this.service = service;
		void this.load();
	}

	dispose(): void {
		this.#abort?.abort();
	}
}
