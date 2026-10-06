// Central reactive state for the Log metrics settings page - same shape as
// `$lib/pipeline-rules/state.svelte.ts`, plus `draft`: a LogFilter carried over from the Logs
// explorer's "Create metric" action, used to pre-fill the create form.

import type { LogFilter } from '$lib/api';
import {
	listLogMetrics,
	createLogMetric,
	updateLogMetric,
	deleteLogMetric,
	type LogMetric,
	type LogMetricInput
} from '$lib/log-metrics-api';

export class LogMetricsState {
	metrics = $state.raw<LogMetric[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** Drives the create/edit dialog - `null` closed, `'new'` creating, a `LogMetric` editing it. */
	formTarget = $state<LogMetric | 'new' | null>(null);
	/** Filter to pre-fill a `'new'` form with; cleared when the form closes. */
	draft = $state.raw<LogFilter | null>(null);
	saving = $state(false);
	saveError = $state<string | null>(null);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.metrics = await listLogMetrics();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	openCreate(draft: LogFilter | null = null): void {
		this.saveError = null;
		this.draft = draft;
		this.formTarget = 'new';
	}

	openEdit(metric: LogMetric): void {
		this.saveError = null;
		this.draft = null;
		this.formTarget = metric;
	}

	closeForm(): void {
		this.formTarget = null;
		this.draft = null;
	}

	async save(id: string | null, input: LogMetricInput): Promise<void> {
		this.saving = true;
		this.saveError = null;
		try {
			if (id) await updateLogMetric(id, input);
			else await createLogMetric(input);
			this.closeForm();
			await this.load();
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.saving = false;
		}
	}

	async setEnabled(metric: LogMetric, enabled: boolean): Promise<void> {
		try {
			await updateLogMetric(metric.id, {
				name: metric.name,
				description: metric.description,
				enabled,
				metricName: metric.metricName,
				condition: metric.condition,
				groupBy: metric.groupBy
			});
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	async remove(id: string): Promise<void> {
		try {
			await deleteLogMetric(id);
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}
}
