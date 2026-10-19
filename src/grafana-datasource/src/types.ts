import { DataSourceJsonData } from '@grafana/data';
import { DataQuery } from '@grafana/schema';

/** What a query asks Flare for. Each kind maps onto one Flare.Api endpoint; the plugin backend answers it. */
export type QueryKind =
  | 'sessions'
  | 'sessionTimeline'
  | 'errors'
  | 'issues'
  | 'releaseHealth'
  | 'releaseHealthTrend'
  | 'releaseHealthValue'
  | 'releases'
  | 'appStarts'
  | 'screenPerformance';

export type TrendMetric = 'crashFreeSessions' | 'sessions' | 'crashedSessions';

export type ValueMetric =
  | 'crashFreeSessions'
  | 'crashFreeUsers'
  | 'sessions'
  | 'crashedSessions'
  | 'erroredSessions'
  | 'users'
  | 'crashedUsers';

export interface FlareQuery extends DataQuery {
  kind: QueryKind;
  /** Service name(s); comma separated, and a multi-value variable expands to that form. Empty means every service. */
  service?: string;
  /** App version, for the Sessions and app performance queries. */
  version?: string;
  /** Session timeline: the session whose spans are listed. */
  sessionId?: string;
  /** Sessions: only sessions with at least one error. */
  errorsOnly?: boolean;
  /** Errors: how many groups to return. */
  topN?: number;
  /** Release health trend: which series to draw. */
  trendMetric?: TrendMetric;
  /** Release health trend: bucket width in minutes; empty picks one from the dashboard interval. */
  bucketMinutes?: number;
  /** Release health value: the one number reported for each version over the range. */
  valueMetric?: ValueMetric;
  /** Release health value: leave out versions with fewer sessions than this, so a brand new release cannot fire a rule. */
  minSessions?: number;
}

export const DEFAULT_QUERY: Partial<FlareQuery> = {
  kind: 'sessions',
};

export interface FlareDataSourceOptions extends DataSourceJsonData {
  /** Base URL of Flare.Api (the plugin backend on the Grafana server calls it, not the browser). */
  url?: string;
  /** Base URL of the Flare dashboard, used only to link rows back to it. */
  dashboardUrl?: string;
}

/** Stored encrypted by Grafana and read by the plugin backend; never sent to the browser. */
export interface FlareSecureJsonData {
  token?: string;
}

/** What a template variable lists. */
export type VariableKind = 'sessionServices' | 'sessionVersions' | 'errorServices' | 'releaseVersions';

export interface FlareVariableQuery extends DataQuery {
  kind: VariableKind;
  /** Narrows versions to one service. */
  service?: string;
}
