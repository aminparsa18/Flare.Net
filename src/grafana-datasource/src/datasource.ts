import { Observable, from, lastValueFrom } from 'rxjs';
import {
  CoreApp,
  CustomVariableSupport,
  DataFrame,
  DataQueryRequest,
  DataQueryResponse,
  DataSourceApi,
  DataSourceInstanceSettings,
  MetricFindValue,
  TestDataSourceResponse,
  createDataFrame,
  FieldType,
  ScopedVars,
} from '@grafana/data';
import { getBackendSrv, getTemplateSrv, isFetchError } from '@grafana/runtime';

import { VariableQueryEditor } from './components/VariableQueryEditor';
import {
  errorGroupsFrame,
  errorIssuesFrame,
  releaseHealthFrame,
  releaseHealthTrendFrames,
  releasesFrame,
  sessionsFrame,
} from './frames';
import {
  TimeWindow,
  errorsRequest,
  releaseHealthRequest,
  releaseHealthTrendRequest,
  resolveWindow,
  serviceCalls,
  sessionsRequest,
  splitServices,
} from './query';
import {
  AppSessionsResponse,
  DEFAULT_QUERY,
  ErrorIssueListResponse,
  ExceptionFacetValuesResponse,
  ExceptionGroupsResponse,
  FlareDataSourceOptions,
  FlareQuery,
  FlareVariableQuery,
  ReleaseHealthResponse,
  ReleaseHealthTrendResponse,
  ReleaseListResponse,
} from './types';

/** The route declared in plugin.json: Grafana proxies it to the configured Flare URL and adds the token header. */
const ROUTE = 'flare';

class FlareVariableSupport extends CustomVariableSupport<DataSource, FlareVariableQuery, FlareQuery, FlareDataSourceOptions> {
  editor = VariableQueryEditor;

  constructor(private readonly datasource: DataSource) {
    super();
  }

  query(request: DataQueryRequest<FlareVariableQuery>): Observable<DataQueryResponse> {
    return from(
      (async (): Promise<DataQueryResponse> => {
        const target = request.targets[0];
        const values = await this.datasource.variableValues(target, {
          fromMs: request.range.from.valueOf(),
          toMs: request.range.to.valueOf(),
        });
        const frame = createDataFrame({
          refId: target.refId,
          fields: [{ name: 'text', type: FieldType.string, values }],
        });
        return { data: [frame] };
      })()
    );
  }
}

export class DataSource extends DataSourceApi<FlareQuery, FlareDataSourceOptions> {
  private readonly baseUrl: string;
  private readonly dashboardUrl?: string;

  variables: FlareVariableSupport;

  constructor(instanceSettings: DataSourceInstanceSettings<FlareDataSourceOptions>) {
    super(instanceSettings);
    this.baseUrl = `${instanceSettings.url}/${ROUTE}`;
    this.dashboardUrl = instanceSettings.jsonData.dashboardUrl;
    this.variables = new FlareVariableSupport(this);
  }

  getDefaultQuery(_: CoreApp): Partial<FlareQuery> {
    return DEFAULT_QUERY;
  }

  filterQuery(query: FlareQuery): boolean {
    return !!query.kind && !query.hide;
  }

  async query(options: DataQueryRequest<FlareQuery>): Promise<DataQueryResponse> {
    const window: TimeWindow = { fromMs: options.range.from.valueOf(), toMs: options.range.to.valueOf() };
    const results = await Promise.all(options.targets.map((target) => this.runQuery(target, window, options)));
    return { data: results.flat() };
  }

  private interpolate(value: string | undefined, scopedVars: ScopedVars): string {
    return value ? getTemplateSrv().replace(value, scopedVars, 'csv') : '';
  }

  private async runQuery(raw: FlareQuery, window: TimeWindow, options: DataQueryRequest<FlareQuery>): Promise<DataFrame[]> {
    const query: FlareQuery = {
      ...raw,
      service: this.interpolate(raw.service, options.scopedVars),
      version: this.interpolate(raw.version, options.scopedVars),
    };
    const resolved = resolveWindow(window);
    const services = serviceCalls(query.service);

    switch (query.kind) {
      case 'sessions': {
        const responses = await Promise.all(
          services.map((service) => this.post<AppSessionsResponse>('/api/app-sessions/list', sessionsRequest(query, resolved, service)))
        );
        // Several services are merged into one table, which is capped per call rather than overall.
        const merged: AppSessionsResponse = {
          windowMinutes: resolved.windowMinutes,
          sessions: responses.flatMap((r) => r.sessions).sort((a, b) => b.lastSeenUnixMs - a.lastSeenUnixMs),
          truncated: responses.some((r) => r.truncated),
          services: [],
          versions: [],
        };
        return [sessionsFrame(query.refId, merged, { clamped: resolved.clamped, dashboardUrl: this.dashboardUrl })];
      }
      case 'errors': {
        const response = await this.post<ExceptionGroupsResponse>('/api/errors/groups', errorsRequest(query, window));
        return [errorGroupsFrame(query.refId, response, this.dashboardUrl)];
      }
      case 'issues': {
        const response = await this.get<ErrorIssueListResponse>('/api/errors/issues');
        return [errorIssuesFrame(query.refId, response, this.dashboardUrl)];
      }
      case 'releaseHealth': {
        const responses = await Promise.all(
          services.map(async (service) => ({
            service,
            response: await this.post<ReleaseHealthResponse>('/api/app-sessions/release-health', releaseHealthRequest(resolved, service)),
          }))
        );
        return [releaseHealthFrame(query.refId, responses, { clamped: resolved.clamped })];
      }
      case 'releaseHealthTrend': {
        const responses = await Promise.all(
          services.map(async (service) => ({
            service: services.length > 1 ? service : undefined,
            response: await this.post<ReleaseHealthTrendResponse>(
              '/api/app-sessions/release-health/trend',
              releaseHealthTrendRequest(query, resolved, service, options.intervalMs)
            ),
          }))
        );
        return releaseHealthTrendFrames(query.refId, responses, query.trendMetric ?? 'crashFreeSessions', { clamped: resolved.clamped });
      }
      case 'releases': {
        const responses = await Promise.all(
          services.map((service) => this.get<ReleaseListResponse>('/api/releases', service ? { service } : undefined))
        );
        const releases = responses
          .flatMap((r) => r.releases)
          .filter((r) => {
            const deployed = Date.parse(r.deployedAt);
            return deployed >= window.fromMs && deployed <= window.toMs;
          });
        return [releasesFrame(query.refId, { releases })];
      }
      default:
        return [];
    }
  }

  /** The values a template variable of `target.kind` offers, for the dashboard's time range. */
  async variableValues(target: FlareVariableQuery, window: TimeWindow): Promise<string[]> {
    const resolved = resolveWindow(window);
    const service = splitServices(this.interpolate(target.service, {}))[0];
    switch (target.kind) {
      case 'sessionServices':
      case 'sessionVersions': {
        const response = await this.post<AppSessionsResponse>('/api/app-sessions/list', {
          windowMinutes: resolved.windowMinutes,
          endUnixMs: resolved.endUnixMs,
          ...(target.kind === 'sessionVersions' && service ? { service } : {}),
        });
        return target.kind === 'sessionServices' ? response.services : response.versions;
      }
      case 'errorServices': {
        const response = await this.post<ExceptionFacetValuesResponse>('/api/errors/facet-values', {
          filter: { from: new Date(window.fromMs).toISOString(), to: new Date(window.toMs).toISOString() },
          field: 'Service',
        });
        return response.values.map((v) => v.value);
      }
      case 'releaseVersions': {
        const response = await this.get<ReleaseListResponse>('/api/releases', service ? { service } : undefined);
        return [...new Set(response.releases.map((r) => r.version))];
      }
      default:
        return [];
    }
  }

  async metricFindQuery(query: FlareVariableQuery, options?: { range?: { from: { valueOf(): number }; to: { valueOf(): number } } }): Promise<MetricFindValue[]> {
    const now = Date.now();
    const window = { fromMs: options?.range?.from.valueOf() ?? now - 3600_000, toMs: options?.range?.to.valueOf() ?? now };
    return (await this.variableValues(query, window)).map((text) => ({ text }));
  }

  private async post<T>(path: string, body: unknown): Promise<T> {
    return this.call<T>({ method: 'POST', url: `${this.baseUrl}${path}`, data: body });
  }

  private async get<T>(path: string, params?: Record<string, string>): Promise<T> {
    return this.call<T>({ method: 'GET', url: `${this.baseUrl}${path}`, params });
  }

  private async call<T>(request: { method: string; url: string; data?: unknown; params?: Record<string, string> }): Promise<T> {
    try {
      const response = await lastValueFrom(getBackendSrv().fetch<T>({ ...request, showErrorAlert: false }));
      return response.data;
    } catch (err) {
      throw new Error(describeError(err));
    }
  }

  async testDatasource(): Promise<TestDataSourceResponse> {
    try {
      // The cheapest authenticated read: it fails with 401 for a missing or wrong token.
      await this.get<ReleaseListResponse>('/api/releases');
      return { status: 'success', message: 'Connected to Flare.' };
    } catch (err) {
      return { status: 'error', message: err instanceof Error ? err.message : String(err) };
    }
  }
}

/** A message a person can act on, from whatever Grafana's proxy and Flare.Api returned. */
export function describeError(err: unknown): string {
  if (!isFetchError(err)) {
    return err instanceof Error ? err.message : String(err);
  }
  const detail = (typeof err.data === 'object' && err.data !== null && ('detail' in err.data ? (err.data as { detail?: string }).detail : (err.data as { message?: string }).message)) || '';
  switch (err.status) {
    case 401:
      return 'Flare rejected the token (401). Check that it is a valid personal access token (flr_pat_...).';
    case 403:
      return 'The token is not allowed to read this (403).';
    case 404:
      return 'Flare returned 404: is the URL the Flare API base, for example http://flare-api:8080?';
    case 502:
    case 504:
    case 0:
      return 'Grafana could not reach Flare. Check the URL, and that the Grafana server can resolve and connect to it.';
    default:
      return `Flare returned ${err.status}${detail ? `: ${detail}` : err.statusText ? `: ${err.statusText}` : ''}.`;
  }
}
