// Central reactive state for the Pipeline Rules page - mirrors AlertsState's shape
// (`$lib/alerts/state.svelte.ts`): a class with `$state` fields, provided via
// `pipelineRulesContext` (context.ts) rather than passed as props, per this repo's
// svelte-best-practices skill.

import {
	listPipelineRules,
	createPipelineRule,
	updatePipelineRule,
	deletePipelineRule,
	type PipelineRule,
	type PipelineRuleRequest
} from '$lib/pipeline-rules-api';

export class PipelineRulesState {
	rules = $state.raw<PipelineRule[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** Drives the create/edit dialog - `null` closed, `'new'` creating, a `PipelineRule` editing that rule. Same shape as `AlertsState.formTarget`. */
	formTarget = $state<PipelineRule | 'new' | null>(null);
	saving = $state(false);
	saveError = $state<string | null>(null);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			const res = await listPipelineRules();
			this.rules = res.rules;
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

	openEdit(rule: PipelineRule): void {
		this.saveError = null;
		this.formTarget = rule;
	}

	closeForm(): void {
		this.formTarget = null;
	}

	async create(request: PipelineRuleRequest): Promise<void> {
		this.saving = true;
		this.saveError = null;
		try {
			await createPipelineRule(request);
			this.formTarget = null;
			await this.load();
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.saving = false;
		}
	}

	async update(id: string, request: PipelineRuleRequest): Promise<void> {
		this.saving = true;
		this.saveError = null;
		try {
			await updatePipelineRule(id, request);
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
			await deletePipelineRule(id);
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}
}
