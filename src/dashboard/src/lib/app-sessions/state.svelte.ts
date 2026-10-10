// Reactive state for the /sessions page - one row per client-app session (`session.id`), see
// docs-internal/adr/0167-app-sessions-view.md. No polling: each load aggregates the window's
// spans live, so it is an on-demand view with a manual refresh, like /external-apis.

import { getAppPerformance, getAppSessions, getReleaseHealth, type AppPerformance, type AppSession, type ReleaseHealthVersion } from '$lib/app-sessions-api';
import { SERVICES_WINDOW_PRESETS, type ServicesWindowPreset } from '$lib/services/state.svelte';

export type AppSessionsWindowPreset = ServicesWindowPreset;
export const APP_SESSIONS_WINDOW_PRESETS = SERVICES_WINDOW_PRESETS;

export class AppSessionsState {
	windowPreset = $state<AppSessionsWindowPreset>('1h');
	/** '' = all. */
	service = $state('');
	version = $state('');
	errorsOnly = $state(false);

	sessions = $state.raw<AppSession[] | null>(null);
	truncated = $state(false);
	/** The pickers' options, returned unfiltered so choosing one doesn't hide the rest. */
	services = $state.raw<string[]>([]);
	versions = $state.raw<string[]>([]);
	/** Per-version crash-free rates; null until loaded, stays empty if that request fails (the table still shows). */
	releaseHealth = $state.raw<ReleaseHealthVersion[] | null>(null);
	/** App start and per-screen timings; null until loaded, empty if that request fails (the table still shows). */
	performance = $state.raw<AppPerformance | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);

	#abort: AbortController | null = null;

	/** Read at load time and kept: the traces links need the window the table was built from. */
	windowEndMs = $state(0);

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		this.loading = true;
		this.error = null;
		try {
			const minutes = APP_SESSIONS_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 60;
			const [response, health, perf] = await Promise.all([
				getAppSessions({ windowMinutes: minutes, service: this.service, version: this.version, errorsOnly: this.errorsOnly }, abort.signal),
				getReleaseHealth({ windowMinutes: minutes, service: this.service }, abort.signal).catch((err) => {
					if (abort.signal.aborted) throw err;
					return null;
				}),
				getAppPerformance({ windowMinutes: minutes, service: this.service, version: this.version }, abort.signal).catch((err) => {
					if (abort.signal.aborted) throw err;
					return null;
				})
			]);
			if (abort.signal.aborted) return;
			this.releaseHealth = health?.versions ?? [];
			this.performance = perf ?? { windowMinutes: minutes, starts: [], screens: [] };
			this.sessions = response.sessions;
			this.truncated = response.truncated;
			this.services = response.services;
			this.versions = response.versions;
			this.windowEndMs = Date.now();
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	setWindowPreset(preset: AppSessionsWindowPreset): void {
		this.windowPreset = preset;
		void this.load();
	}

	setService(service: string): void {
		this.service = service;
		void this.load();
	}

	setVersion(version: string): void {
		this.version = version;
		void this.load();
	}

	setErrorsOnly(value: boolean): void {
		this.errorsOnly = value;
		void this.load();
	}

	dispose(): void {
		this.#abort?.abort();
	}
}
