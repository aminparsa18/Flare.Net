// Localized display names for panel visualizations/reducers - functions, not a lookup
// object built once, so a live locale switch is reflected (see time-range.ts's remarks).
import type { PanelReducer, PanelVisualization } from '$lib/dashboards/visualization';
import * as m from '$lib/paraglide/messages';

export function visualizationLabel(visualization: PanelVisualization): string {
	switch (visualization) {
		case 'timeSeries':
			return m.panelVisualization_timeSeries();
		case 'bar':
			return m.panelVisualization_bar();
		case 'stackedBar':
			return m.panelVisualization_stackedBar();
		case 'value':
			return m.panelVisualization_value();
		case 'pie':
			return m.panelVisualization_pie();
		case 'table':
			return m.panelVisualization_table();
		case 'histogram':
			return m.panelVisualization_histogram();
	}
}

export function reducerLabel(reducer: PanelReducer): string {
	switch (reducer) {
		case 'last':
			return m.panelVisualization_reducerLast();
		case 'avg':
			return m.panelVisualization_reducerAvg();
		case 'sum':
			return m.panelVisualization_reducerSum();
		case 'min':
			return m.panelVisualization_reducerMin();
		case 'max':
			return m.panelVisualization_reducerMax();
	}
}
