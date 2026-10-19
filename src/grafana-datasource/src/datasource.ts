import { Observable, from } from 'rxjs';
import {
  CoreApp,
  CustomVariableSupport,
  DataQueryRequest,
  DataQueryResponse,
  DataSourceInstanceSettings,
  MetricFindValue,
  ScopedVars,
  FieldType,
  createDataFrame,
} from '@grafana/data';
import { DataSourceWithBackend, getTemplateSrv } from '@grafana/runtime';

import { VariableQueryEditor } from './components/VariableQueryEditor';
import { DEFAULT_QUERY, FlareDataSourceOptions, FlareQuery, FlareVariableQuery } from './types';

/** A dashboard time range in Unix milliseconds, as the backend's variables resource takes it. */
interface TimeWindow {
  fromMs: number;
  toMs: number;
}

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

/**
 * The Flare data source. The queries run in the plugin backend (`pkg/`), which calls Flare.Api with the stored
 * token, so they also work in alert rules, public dashboards and reports; this class only prepares the query.
 */
export class DataSource extends DataSourceWithBackend<FlareQuery, FlareDataSourceOptions> {
  variables: FlareVariableSupport;

  constructor(instanceSettings: DataSourceInstanceSettings<FlareDataSourceOptions>) {
    super(instanceSettings);
    this.variables = new FlareVariableSupport(this);
    // The Releases query is annotation-shaped (time, title, text), so the standard annotation mapping applies.
    this.annotations = {};
  }

  getDefaultQuery(_: CoreApp): Partial<FlareQuery> {
    return DEFAULT_QUERY;
  }

  filterQuery(query: FlareQuery): boolean {
    return !!query.kind && !query.hide;
  }

  /** Dashboard variables in the text fields; `csv` is what a multi-value variable needs to read as a service list. */
  applyTemplateVariables(query: FlareQuery, scopedVars: ScopedVars): FlareQuery {
    const interpolate = (value: string | undefined) => (value ? getTemplateSrv().replace(value, scopedVars, 'csv') : '');
    return {
      ...query,
      service: interpolate(query.service),
      version: interpolate(query.version),
      sessionId: interpolate(query.sessionId),
    };
  }

  /** The values a template variable of `target.kind` offers, for the dashboard's time range. */
  async variableValues(target: FlareVariableQuery, window: TimeWindow): Promise<string[]> {
    const response = await this.postResource<{ values: string[] }>('variables', {
      kind: target.kind,
      service: getTemplateSrv().replace(target.service ?? '', {}, 'csv'),
      from: window.fromMs,
      to: window.toMs,
    });
    return response.values;
  }

  async metricFindQuery(
    query: FlareVariableQuery,
    options?: { range?: { from: { valueOf(): number }; to: { valueOf(): number } } }
  ): Promise<MetricFindValue[]> {
    const now = Date.now();
    const window = { fromMs: options?.range?.from.valueOf() ?? now - 3600_000, toMs: options?.range?.to.valueOf() ?? now };
    return (await this.variableValues(query, window)).map((text) => ({ text }));
  }
}
