// State for the /frontend page (ADR-0153): browser web vitals and JS errors, composed from the
// existing metrics and errors queries - no endpoint of its own. The signals follow ADR-0151:
// `browser.web_vital.<name>` histograms with a `rating` attribute, and JS errors as exception
// span events from resources carrying `telemetry.sdk.language = webjs`.

import { getExceptionGroups, type ExceptionGroup } from '$lib/errors-api';
import { buildErrorsDeepLinkHref } from '$lib/deep-links';
import { queryMetric, type MetricQueryResponse } from '$lib/metrics-api';
import { SERVICES_WINDOW_PRESETS, type ServicesWindowPreset } from '$lib/services/state.svelte';

export const WEB_VITALS = ['lcp', 'inp', 'cls', 'fcp', 'ttfb'] as const;
export type WebVital = (typeof WEB_VITALS)[number];

/** Google's "good" / "poor" boundaries at p75, in the vital's own unit (ms; CLS is unitless). */
export const VITAL_THRESHOLDS: Record<WebVital, { good: number; poor: number }> = {
	lcp: { good: 2500, poor: 4000 },
	inp: { good: 200, poor: 500 },
	cls: { good: 0.1, poor: 0.25 },
	fcp: { good: 1800, poor: 3000 },
	ttfb: { good: 800, poor: 1800 }
};

export type VitalRating = 'good' | 'needs-improvement' | 'poor';

export function rateVital(vital: WebVital, p75: number): VitalRating {
	const t = VITAL_THRESHOLDS[vital];
	return p75 <= t.good ? 'good' : p75 <= t.poor ? 'needs-improvement' : 'poor';
}

export interface VitalCell {
	p75: number;
	samples: number;
	/** Share of samples the browser itself rated "good", 0-1; null when no `rating` attribute was sent. */
	goodShare: number | null;
}

export interface ServiceVitals {
	service: string;
	cells: Partial<Record<WebVital, VitalCell>>;
}

export interface RouteVitals {
	route: string;
	cells: Partial<Record<WebVital, VitalCell>>;
}

export const BROWSER_RESOURCE_FILTER = { key: 'telemetry.sdk.language', value: 'webjs' } as const;

/** Grouping by an attribute no vital carries collapses each service to a single series. */
const ALL_KEY = '__all__';

/** Route template attribute on the vital histograms (ADR-0154); the same semconv key ADR-0071 reads for HTTP endpoints. */
export const ROUTE_KEY = 'url.template';

export class FrontendState {
	windowPreset = $state<ServicesWindowPreset>('24h');
	services = $state.raw<ServiceVitals[] | null>(null);
	errors = $state.raw<ExceptionGroup[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** The window the data was fetched for - the Errors deep links reuse it. */
	range = $state.raw<{ from: Date; to: Date } | null>(null);

	/** Per-route vitals by service, loaded when a service row is expanded. `null` = loading. */
	routes = $state.raw<Record<string, RouteVitals[] | null>>({});
	routeError = $state<string | null>(null);

	#abort: AbortController | null = null;

	get totalErrors(): number {
		return this.errors.reduce((sum, g) => sum + g.occurrenceCount, 0);
	}

	setWindowPreset(preset: ServicesWindowPreset): void {
		this.windowPreset = preset;
		void this.load();
	}

	/** `/errors?state=` limited to browser errors over the loaded window, optionally to one group. */
	errorsHref(group?: ExceptionGroup): string | null {
		if (!this.range) return null;
		return buildErrorsDeepLinkHref({
			customRange: this.range,
			services: [],
			resourceAttributes: [{ ...BROWSER_RESOURCE_FILTER }],
			exceptionType: group?.exceptionType ?? '',
			exceptionMessage: group?.exceptionMessage ?? ''
		});
	}

	/** Loads one service's vitals grouped by the route attribute (ADR-0154) over the loaded window. */
	async loadRoutes(service: string): Promise<void> {
		if (!this.range || service in this.routes) return;
		this.routes = { ...this.routes, [service]: null };
		this.routeError = null;
		const { from, to } = this.range;
		const filter = { from: from.toISOString(), to: to.toISOString(), services: [service] };
		const bucketWidthSeconds = Math.max(60, Math.round((to.getTime() - from.getTime()) / 1000));
		try {
			const responses = await Promise.all(
				WEB_VITALS.map((v) =>
					queryMetric({ metricName: `browser.web_vital.${v}`, type: 'Histogram', filter, bucketWidthSeconds, groupByAttributeKey: ROUTE_KEY, topN: 200 })
				)
			);
			this.routes = { ...this.routes, [service]: buildRouteVitals(responses) };
		} catch (err) {
			const { [service]: _, ...rest } = this.routes;
			this.routes = rest;
			this.routeError = err instanceof Error ? err.message : String(err);
		}
	}

	dispose(): void {
		this.#abort?.abort();
	}

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;
		const { signal } = abort;

		this.loading = true;
		this.error = null;
		try {
			const minutes = SERVICES_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 1440;
			const to = new Date();
			const from = new Date(to.getTime() - minutes * 60_000);
			const filter = { from: from.toISOString(), to: to.toISOString() };
			// One bucket spanning the window, so each series' single point is the window's aggregate.
			const bucketWidthSeconds = minutes * 60;

			const [overall, byRating, groups] = await Promise.all([
				Promise.all(
					WEB_VITALS.map((v) =>
						queryMetric(
							{ metricName: `browser.web_vital.${v}`, type: 'Histogram', filter, bucketWidthSeconds, groupByAttributeKey: ALL_KEY, topN: 100 },
							signal
						)
					)
				),
				Promise.all(
					WEB_VITALS.map((v) =>
						queryMetric(
							{ metricName: `browser.web_vital.${v}`, type: 'Histogram', filter, bucketWidthSeconds, groupByAttributeKey: 'rating', topN: 200 },
							signal
						)
					)
				),
				getExceptionGroups({ filter: { ...filter, resourceAttributes: [{ ...BROWSER_RESOURCE_FILTER }] }, topN: 50 }, signal)
			]);
			if (signal.aborted) return;

			this.range = { from, to };
			this.services = buildServiceVitals(overall, byRating);
			this.errors = groups.groups;
		} catch (err) {
			if (signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!signal.aborted) this.loading = false;
		}
	}
}

/** Merges the per-vital overall percentile series with the per-rating sample counts into one row per service. */
export function buildServiceVitals(overall: MetricQueryResponse[], byRating: MetricQueryResponse[]): ServiceVitals[] {
	const rows = new Map<string, ServiceVitals>();
	const row = (service: string): ServiceVitals => {
		let r = rows.get(service);
		if (!r) rows.set(service, (r = { service, cells: {} }));
		return r;
	};

	WEB_VITALS.forEach((vital, i) => {
		for (const series of overall[i]?.series ?? []) {
			// Single bucket; if the window straddled two, the later one is the more recent signal.
			const point = series.points[series.points.length - 1];
			if (point?.p75 == null) continue;
			row(series.serviceName).cells[vital] = {
				p75: point.p75,
				samples: series.points.reduce((sum, p) => sum + (p.count ?? 0), 0),
				goodShare: null
			};
		}

		const totals = new Map<string, { good: number; all: number; rated: boolean }>();
		for (const series of byRating[i]?.series ?? []) {
			const count = series.points.reduce((sum, p) => sum + (p.count ?? 0), 0);
			const t = totals.get(series.serviceName) ?? { good: 0, all: 0, rated: false };
			t.all += count;
			if (series.attributes.rating) t.rated = true;
			if (series.attributes.rating === 'good') t.good += count;
			totals.set(series.serviceName, t);
		}
		for (const [service, t] of totals) {
			const cell = rows.get(service)?.cells[vital];
			if (cell && t.rated && t.all > 0) cell.goodShare = t.good / t.all;
		}
	});

	return [...rows.values()].sort((a, b) => a.service.localeCompare(b.service));
}

/** One row per route template; measurements without the attribute are listed under the empty route. */
export function buildRouteVitals(responses: MetricQueryResponse[]): RouteVitals[] {
	const rows = new Map<string, RouteVitals>();
	WEB_VITALS.forEach((vital, i) => {
		for (const series of responses[i]?.series ?? []) {
			const point = series.points[series.points.length - 1];
			if (point?.p75 == null) continue;
			const route = series.attributes[ROUTE_KEY] ?? '';
			let row = rows.get(route);
			if (!row) rows.set(route, (row = { route, cells: {} }));
			row.cells[vital] = { p75: point.p75, samples: series.points.reduce((sum, p) => sum + (p.count ?? 0), 0), goodShare: null };
		}
	});
	const loads = (r: RouteVitals) => r.cells.fcp?.samples ?? 0;
	return [...rows.values()].sort((a, b) => loads(b) - loads(a));
}

/** Page loads: FCP fires once per page load, so its sample count is the closest metric-form count. */
export function pageLoads(cells: Partial<Record<WebVital, VitalCell>>): number {
	return cells.fcp?.samples ?? Math.max(0, ...WEB_VITALS.map((v) => cells[v]?.samples ?? 0));
}
