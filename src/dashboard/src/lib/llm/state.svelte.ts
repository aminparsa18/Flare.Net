// Central reactive state for the /llm page - one row per (provider, model) from spans'
// `gen_ai.*` attributes. See docs-internal/adr/0100-llm-observability-genai-spans.md.
//
// No polling, same as ExternalApisState: every load aggregates the window's model-call spans
// live, so it's an on-demand view with a manual refresh.

import { getLlmModels, type LlmModel } from '$lib/llm-api';
import { SERVICES_WINDOW_PRESETS, type ServicesWindowPreset } from '$lib/services/state.svelte';

export type LlmWindowPreset = ServicesWindowPreset;
export const LLM_WINDOW_PRESETS = SERVICES_WINDOW_PRESETS;

export type LlmSortColumn = 'model' | 'perSecond' | 'errorRate' | 'p95Ms' | 'p99Ms' | 'inputTokens' | 'outputTokens' | 'lastSeenUnixMs';

/** Errors over calls, 0-1. */
export function errorRate(row: { callCount: number; errorCount: number }): number {
	return row.callCount === 0 ? 0 : row.errorCount / row.callCount;
}

export class LlmState {
	windowPreset = $state<LlmWindowPreset>('1h');
	/** '' = all calling services. */
	service = $state('');

	models = $state.raw<LlmModel[] | null>(null);
	/** The service picker's options - returned unfiltered by the current service. */
	services = $state.raw<string[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	sortColumn = $state<LlmSortColumn>('perSecond');
	sortDescending = $state(true);

	#abort: AbortController | null = null;

	#minutes(): number {
		return LLM_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 60;
	}

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		this.loading = true;
		this.error = null;
		try {
			const response = await getLlmModels(this.#minutes(), this.service, abort.signal);
			if (abort.signal.aborted) return;
			this.models = response.models;
			this.services = response.services;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	/** A filter/window change is a different query - clear so the spinner shows. */
	#reload(): void {
		this.models = null;
		void this.load();
	}

	setWindowPreset(preset: LlmWindowPreset): void {
		if (this.windowPreset === preset) return;
		this.windowPreset = preset;
		this.#reload();
	}

	setService(service: string): void {
		if (this.service === service) return;
		this.service = service;
		this.#reload();
	}

	setSort(column: LlmSortColumn): void {
		if (this.sortColumn === column) {
			this.sortDescending = !this.sortDescending;
		} else {
			this.sortColumn = column;
			this.sortDescending = column !== 'model';
		}
	}

	sorted(): LlmModel[] {
		const column = this.sortColumn;
		const direction = this.sortDescending ? -1 : 1;
		return [...(this.models ?? [])].sort((a, b) => {
			switch (column) {
				case 'model':
					return direction * (a.model.localeCompare(b.model) || a.provider.localeCompare(b.provider));
				case 'errorRate':
					return direction * (errorRate(a) - errorRate(b));
				default:
					return direction * (a[column] - b[column]);
			}
		});
	}

	dispose(): void {
		this.#abort?.abort();
	}
}
