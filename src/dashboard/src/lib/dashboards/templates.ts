// Built-in dashboard templates (docs-internal/adr/0088-built-in-dashboard-templates.md): a
// few ready-made, Metrics-only dashboards for what .NET apps, the OTel collector's
// hostmetrics receiver and Kubernetes emit by default. Installing one just creates an
// ordinary dashboard from `buildTemplateLayout()` - after that it's a normal, editable
// dashboard like any imported one, with no link back to the template.
//
// Every panel's `selectedMetric` deliberately omits `serviceName`: the template can't know
// which services emit the metric, and `MetricsExplorerState.applySavedViewState` resolves a
// service-less selection to the first picker entry with that name and type (narrowed by the
// panel's `services`, which the dashboard's Service variable fills - see
// DashboardMetricsPanelBody). So a metric name or type that doesn't match what the app really
// emits leaves its panel on "no metric" - the names/types below are the OTel semantic-
// convention ones the example shop (OpenTelemetry.Instrumentation.AspNetCore/Http/Runtime
// and the collector's hostmetrics/kubeletstats receivers) actually sends.
//
// Group-by keys are *data point* attributes only (MetricSeriesQueryBuilder groups on
// DataPointAttributes), so resource attributes such as `k8s.pod.name` can't be used here.
// Likewise an `Attribute`-target variable has nothing to attach to on a Metrics panel, so
// the only variable a template carries is Service.

import type { DashboardLayout, DashboardPanel, DashboardRow, DashboardVariable } from '$lib/dashboards-api';
import type { MetricsSavedViewState } from '$lib/metrics/state.svelte';
import type { MetricPointType } from '$lib/metrics-api';
import type { PanelVisualization } from './visualization';
import { GRID_COLUMNS } from './layout';
import * as m from '$lib/paraglide/messages';

interface TemplatePanel {
	title: string;
	description?: string;
	metric: string;
	type: MetricPointType;
	/** Data point attribute to split the chart into one line per value. */
	groupBy?: string;
	visualization?: PanelVisualization;
	/** Grid columns, default half width. */
	w?: number;
}

interface TemplateSection {
	/** Becomes a collapsible row; omitted for the ungrouped area at the top. */
	title?: string;
	panels: TemplatePanel[];
}

export interface DashboardTemplate {
	id: string;
	name: () => string;
	description: () => string;
	sections: TemplateSection[];
}

const PANEL_HEIGHT = 4;

export const DASHBOARD_TEMPLATES: readonly DashboardTemplate[] = [
	{
		id: 'aspnetcore',
		name: () => m.dashboardTemplate_aspnetcore_name(),
		description: () => m.dashboardTemplate_aspnetcore_description(),
		sections: [
			{
				panels: [
					{ title: 'Request duration', description: 'http.server.request.duration percentiles.', metric: 'http.server.request.duration', type: 'Histogram' },
					{ title: 'Request duration heatmap', metric: 'http.server.request.duration', type: 'Histogram', visualization: 'heatmap' },
					{ title: 'Active requests', metric: 'http.server.active_requests', type: 'Sum' },
					{ title: 'Kestrel active connections', metric: 'kestrel.active_connections', type: 'Sum' },
					{ title: 'Unhandled exceptions', metric: 'aspnetcore.diagnostics.exceptions', type: 'Sum', groupBy: 'error.type' },
					{ title: 'Rate-limited requests', metric: 'aspnetcore.rate_limiting.requests', type: 'Sum', groupBy: 'aspnetcore.rate_limiting.result' }
				]
			}
		]
	},
	{
		id: 'httpclient',
		name: () => m.dashboardTemplate_httpclient_name(),
		description: () => m.dashboardTemplate_httpclient_description(),
		sections: [
			{
				panels: [
					{ title: 'Outbound request duration', metric: 'http.client.request.duration', type: 'Histogram' },
					{ title: 'Outbound request heatmap', metric: 'http.client.request.duration', type: 'Histogram', visualization: 'heatmap' },
					{ title: 'Active requests', metric: 'http.client.active_requests', type: 'Sum' },
					{ title: 'Open connections', metric: 'http.client.open_connections', type: 'Sum', groupBy: 'http.connection.state' },
					{ title: 'Time in queue', metric: 'http.client.request.time_in_queue', type: 'Histogram' },
					{ title: 'DNS lookup duration', metric: 'dns.lookup.duration', type: 'Histogram' }
				]
			}
		]
	},
	{
		id: 'dotnet-runtime',
		name: () => m.dashboardTemplate_dotnetRuntime_name(),
		description: () => m.dashboardTemplate_dotnetRuntime_description(),
		sections: [
			{
				title: 'Garbage collector',
				panels: [
					{ title: 'GC collections', metric: 'dotnet.gc.collections', type: 'Sum', groupBy: 'gc.heap.generation' },
					{ title: 'Heap size after last GC', metric: 'dotnet.gc.last_collection.heap.size', type: 'Sum', groupBy: 'gc.heap.generation' },
					{ title: 'Allocated bytes', metric: 'dotnet.gc.heap.total_allocated', type: 'Sum' },
					{ title: 'GC pause time', metric: 'dotnet.gc.pause.time', type: 'Sum' }
				]
			},
			{
				title: 'Thread pool',
				panels: [
					{ title: 'Thread count', metric: 'dotnet.thread_pool.thread.count', type: 'Sum' },
					{ title: 'Queue length', metric: 'dotnet.thread_pool.queue.length', type: 'Sum' },
					{ title: 'Completed work items', metric: 'dotnet.thread_pool.work_item.count', type: 'Sum' },
					{ title: 'Lock contentions', metric: 'dotnet.monitor.lock_contentions', type: 'Sum' }
				]
			},
			{
				title: 'Process',
				panels: [
					{ title: 'Exceptions', metric: 'dotnet.exceptions', type: 'Sum', groupBy: 'error.type' },
					{ title: 'Working set', metric: 'dotnet.process.memory.working_set', type: 'Sum' },
					{ title: 'CPU time', metric: 'dotnet.process.cpu.time', type: 'Sum', groupBy: 'cpu.mode' }
				]
			}
		]
	},
	{
		id: 'hostmetrics',
		name: () => m.dashboardTemplate_hostmetrics_name(),
		description: () => m.dashboardTemplate_hostmetrics_description(),
		sections: [
			{
				panels: [
					{ title: 'CPU time', metric: 'system.cpu.time', type: 'Sum', groupBy: 'state' },
					{ title: 'Memory usage', metric: 'system.memory.usage', type: 'Sum', groupBy: 'state' },
					{ title: 'Load average (1m)', metric: 'system.cpu.load_average.1m', type: 'Gauge' },
					{ title: 'Load average (5m)', metric: 'system.cpu.load_average.5m', type: 'Gauge' },
					{ title: 'Filesystem usage', metric: 'system.filesystem.usage', type: 'Sum', groupBy: 'state' },
					{ title: 'Disk I/O', metric: 'system.disk.io', type: 'Sum', groupBy: 'direction' },
					{ title: 'Network I/O', metric: 'system.network.io', type: 'Sum', groupBy: 'direction' }
				]
			}
		]
	},
	{
		id: 'kubernetes',
		name: () => m.dashboardTemplate_kubernetes_name(),
		description: () => m.dashboardTemplate_kubernetes_description(),
		sections: [
			{
				title: 'Nodes',
				panels: [
					{ title: 'Node CPU usage', metric: 'k8s.node.cpu.usage', type: 'Gauge' },
					{ title: 'Node memory working set', metric: 'k8s.node.memory.working_set', type: 'Gauge' }
				]
			},
			{
				title: 'Pods',
				panels: [
					{ title: 'Pod CPU usage', metric: 'k8s.pod.cpu.usage', type: 'Gauge' },
					{ title: 'Pod memory working set', metric: 'k8s.pod.memory.working_set', type: 'Gauge' },
					{ title: 'Pod CPU vs limit', metric: 'k8s.pod.cpu_limit_utilization', type: 'Gauge' },
					{ title: 'Pod memory vs limit', metric: 'k8s.pod.memory_limit_utilization', type: 'Gauge' },
					{ title: 'Pod network I/O', metric: 'k8s.pod.network.io', type: 'Sum', groupBy: 'direction' }
				]
			},
			{
				title: 'Workloads',
				panels: [
					{ title: 'Deployments available', metric: 'k8s.deployment.available', type: 'Gauge' },
					{ title: 'Deployments desired', metric: 'k8s.deployment.desired', type: 'Gauge' }
				]
			}
		]
	}
];

/** Same Service-narrowing variable every template carries - see the header comment for why it's the only one. */
function serviceVariable(): DashboardVariable {
	return { id: crypto.randomUUID(), name: 'Service', target: 'Service', sourceKind: 'Query' };
}

function metricsQuery(panel: TemplatePanel): MetricsSavedViewState {
	return {
		timeRangePreset: '1h',
		customRange: null,
		services: [],
		compareEnabled: false,
		groupByAttributeKey: panel.groupBy ?? null,
		topN: 20,
		havingOperator: null,
		havingValue: null,
		postProcessFunctions: [],
		timeShiftSeconds: null,
		selectedMetric: { metricName: panel.metric, type: panel.type }
	};
}

/** Lays `panels` out left to right, wrapping at GRID_COLUMNS - y is relative to the section's own grid, same as `DashboardPanel.layout` under a row. */
function placePanels(panels: readonly TemplatePanel[], rowId: string | null): DashboardPanel[] {
	let x = 0;
	let y = 0;
	return panels.map((spec) => {
		const w = Math.min(GRID_COLUMNS, spec.w ?? GRID_COLUMNS / 2);
		if (x + w > GRID_COLUMNS) {
			x = 0;
			y += PANEL_HEIGHT;
		}
		const panel: DashboardPanel = {
			id: crypto.randomUUID(),
			panelType: 'Metrics',
			title: spec.title,
			description: spec.description,
			layout: { x, y, w, h: PANEL_HEIGHT },
			query: metricsQuery(spec),
			visualization: spec.visualization,
			rowId
		};
		x += w;
		return panel;
	});
}

/** A fresh layout (new panel/row/variable ids every call, so installing twice never shares ids) for `template`. */
export function buildTemplateLayout(template: DashboardTemplate): DashboardLayout {
	const panels: DashboardPanel[] = [];
	const rows: DashboardRow[] = [];
	for (const section of template.sections) {
		if (section.title) {
			const row: DashboardRow = { id: crypto.randomUUID(), title: section.title };
			rows.push(row);
			panels.push(...placePanels(section.panels, row.id));
		} else {
			panels.push(...placePanels(section.panels, null));
		}
	}
	return { panels, variables: [serviceVariable()], rows };
}

export function templatePanelCount(template: DashboardTemplate): number {
	return template.sections.reduce((sum, s) => sum + s.panels.length, 0);
}
