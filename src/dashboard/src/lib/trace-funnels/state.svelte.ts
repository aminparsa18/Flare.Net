// Central reactive state for the /traces/funnels page - an ordered list of span-match steps,
// the per-step figures for the window, and one step's drill-down (the traces behind a
// reached/dropped/errored figure). See docs-internal/adr/0067-trace-funnels.md.
//
// Runs on demand (the Run button, or a window change once a result exists), never on each
// keystroke: every run groups the window's matching spans by trace live, which is too heavy
// to repeat while a step is half-typed.

import {
	getTraceFunnel,
	getTraceFunnelTraces,
	isStepDefined,
	MAX_FUNNEL_STEPS,
	MIN_FUNNEL_STEPS,
	type TraceFunnelOutcome,
	type TraceFunnelResponse,
	type TraceFunnelStep,
	type TraceFunnelTrace
} from '$lib/trace-funnels-api';
import { SERVICES_WINDOW_PRESETS, type ServicesWindowPreset } from '$lib/services/state.svelte';

// Same five presets as the Services tab and Messaging page - the server clamps to the same
// 5m-24h range (TraceFunnelQueryBuilder.MinWindowMinutes/MaxWindowMinutes).
export type FunnelWindowPreset = ServicesWindowPreset;
export const FUNNEL_WINDOW_PRESETS = SERVICES_WINDOW_PRESETS;

/** A step plus a stable key for `{#each}` - the wire/saved shape has no id. */
export interface EditableStep extends TraceFunnelStep {
	id: number;
}

/** What a saved view of this page stores (`SavedViewPageType.Funnels`). */
export interface FunnelViewState {
	steps: TraceFunnelStep[];
	windowPreset: FunnelWindowPreset;
}

export interface FunnelDrill {
	stepIndex: number;
	outcome: TraceFunnelOutcome;
}

let nextStepId = 1;

function editable(step?: Partial<TraceFunnelStep>): EditableStep {
	return {
		id: nextStepId++,
		serviceName: step?.serviceName ?? '',
		spanName: step?.spanName ?? '',
		attributes: step?.attributes ? [...step.attributes] : []
	};
}

function plain(step: EditableStep): TraceFunnelStep {
	return { serviceName: step.serviceName, spanName: step.spanName, attributes: step.attributes };
}

export class TraceFunnelState {
	steps = $state<EditableStep[]>([editable(), editable()]);
	windowPreset = $state<FunnelWindowPreset>('1h');

	result = $state.raw<TraceFunnelResponse | null>(null);
	/** The steps `result` was computed for - labels and drill-downs read these, not the live editor, so editing a step doesn't relabel stale figures. */
	resultSteps = $state.raw<TraceFunnelStep[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	drill = $state.raw<FunnelDrill | null>(null);
	drillTraces = $state.raw<TraceFunnelTrace[] | null>(null);
	drillLoading = $state(false);
	drillError = $state<string | null>(null);

	#abort: AbortController | null = null;
	#drillAbort: AbortController | null = null;

	get canAddStep(): boolean {
		return this.steps.length < MAX_FUNNEL_STEPS;
	}

	get canRemoveStep(): boolean {
		return this.steps.length > MIN_FUNNEL_STEPS;
	}

	/** Every step has something to match on - the Run button's enabled state. */
	get runnable(): boolean {
		return this.steps.every(isStepDefined);
	}

	minutes(): number {
		return FUNNEL_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 60;
	}

	addStep(): void {
		if (this.canAddStep) this.steps.push(editable());
	}

	removeStep(id: number): void {
		if (this.canRemoveStep) this.steps = this.steps.filter((s) => s.id !== id);
	}

	moveStep(id: number, delta: -1 | 1): void {
		const from = this.steps.findIndex((s) => s.id === id);
		const to = from + delta;
		if (from < 0 || to < 0 || to >= this.steps.length) return;
		const next = [...this.steps];
		[next[from], next[to]] = [next[to], next[from]];
		this.steps = next;
	}

	updateStep(id: number, patch: Partial<TraceFunnelStep>): void {
		const step = this.steps.find((s) => s.id === id);
		if (step) Object.assign(step, patch);
	}

	setWindowPreset(preset: FunnelWindowPreset): void {
		if (this.windowPreset === preset) return;
		this.windowPreset = preset;
		if (this.result != null && this.runnable) void this.run();
	}

	async run(): Promise<void> {
		if (!this.runnable) return;
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		const steps = this.steps.map(plain);
		this.loading = true;
		this.error = null;
		this.closeDrill();
		try {
			const response = await getTraceFunnel(steps, this.minutes(), abort.signal);
			if (abort.signal.aborted) return;
			this.result = response;
			this.resultSteps = steps;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	openDrill(stepIndex: number, outcome: TraceFunnelOutcome): void {
		this.drill = { stepIndex, outcome };
		this.drillTraces = null;
		void this.#loadDrill();
	}

	closeDrill(): void {
		this.#drillAbort?.abort();
		this.drill = null;
		this.drillTraces = null;
		this.drillError = null;
		this.drillLoading = false;
	}

	async #loadDrill(): Promise<void> {
		const drill = this.drill;
		if (drill == null) return;

		this.#drillAbort?.abort();
		const abort = new AbortController();
		this.#drillAbort = abort;

		this.drillLoading = true;
		this.drillError = null;
		try {
			const traces = await getTraceFunnelTraces(this.resultSteps, this.minutes(), drill.stepIndex, drill.outcome, abort.signal);
			if (abort.signal.aborted) return;
			this.drillTraces = traces;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.drillError = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.drillLoading = false;
		}
	}

	currentViewState(): FunnelViewState {
		return { steps: this.steps.map(plain), windowPreset: this.windowPreset };
	}

	/** Loads a saved funnel and runs it. Tolerates a malformed/partial state by falling back to defaults field by field. */
	applySavedViewState(state: unknown): void {
		const saved = (state ?? {}) as Partial<FunnelViewState>;
		const steps = Array.isArray(saved.steps) ? saved.steps.slice(0, MAX_FUNNEL_STEPS).map((s) => editable(s)) : [];
		while (steps.length < MIN_FUNNEL_STEPS) steps.push(editable());
		this.steps = steps;
		if (saved.windowPreset && FUNNEL_WINDOW_PRESETS.some((p) => p.value === saved.windowPreset)) {
			this.windowPreset = saved.windowPreset;
		}
		this.result = null;
		void this.run();
	}

	dispose(): void {
		this.#abort?.abort();
		this.#drillAbort?.abort();
	}
}

/** A step's one-line description for the results table - service, span name, then each attribute filter, `·`-separated. */
export function stepLabel(step: TraceFunnelStep): string {
	const parts: string[] = [];
	if (step.serviceName) parts.push(step.serviceName);
	if (step.spanName) parts.push(step.spanName);
	for (const a of step.attributes) {
		switch (a.operator ?? 'Equals') {
			case 'Exists':
				parts.push(`${a.key} ∃`);
				break;
			case 'Absent':
				parts.push(`${a.key} ∄`);
				break;
			case 'NotEquals':
				parts.push(`${a.key} ≠ ${a.value}`);
				break;
			case 'Regex':
				parts.push(`${a.key} ~ ${a.value}`);
				break;
			case 'NotRegex':
				parts.push(`${a.key} !~ ${a.value}`);
				break;
			case 'In':
				parts.push(`${a.key} ∈ [${(a.values ?? []).join(', ')}]`);
				break;
			case 'NotIn':
				parts.push(`${a.key} ∉ [${(a.values ?? []).join(', ')}]`);
				break;
			default:
				parts.push(`${a.key} = ${a.value}`);
		}
	}
	return parts.join(' · ');
}
