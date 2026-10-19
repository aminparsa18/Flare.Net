import { DataSourceJsonData } from '@grafana/data';
import { DataQuery } from '@grafana/schema';

/** What a query asks Flare for. Each kind maps onto one Flare.Api endpoint. */
export type QueryKind = 'sessions' | 'errors' | 'issues' | 'releaseHealth' | 'releaseHealthTrend' | 'releases';

export type TrendMetric = 'crashFreeSessions' | 'sessions' | 'crashedSessions';

export interface FlareQuery extends DataQuery {
  kind: QueryKind;
  /** Service name(s); comma separated, and a multi-value variable expands to that form. Empty means every service. */
  service?: string;
  /** App version, for the Sessions query. */
  version?: string;
  /** Sessions: only sessions with at least one error. */
  errorsOnly?: boolean;
  /** Errors: how many groups to return. */
  topN?: number;
  /** Release health trend: which series to draw. */
  trendMetric?: TrendMetric;
  /** Release health trend: bucket width in minutes; empty picks one from the dashboard interval. */
  bucketMinutes?: number;
}

export const DEFAULT_QUERY: Partial<FlareQuery> = {
  kind: 'sessions',
};

export interface FlareDataSourceOptions extends DataSourceJsonData {
  /** Base URL of Flare.Api (the Grafana server calls it, not the browser). */
  url?: string;
  /** Base URL of the Flare dashboard, used only to link rows back to it. */
  dashboardUrl?: string;
}

/** Stored encrypted by Grafana and attached to proxied requests by the plugin route; never sent to the browser. */
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

// Wire shapes of the Flare.Api responses the plugin reads (camelCase JSON, enums as strings).

export interface AppSession {
  sessionId: string;
  serviceName: string;
  version: string;
  os: string;
  device: string;
  firstSeenUnixMs: number;
  lastSeenUnixMs: number;
  spanCount: number;
  traceCount: number;
  errorCount: number;
  screens: string[];
}

export interface AppSessionsResponse {
  windowMinutes: number;
  sessions: AppSession[];
  truncated: boolean;
  services: string[];
  versions: string[];
}

export interface ReleaseHealthVersion {
  version: string;
  sessions: number;
  crashedSessions: number;
  erroredSessions: number;
  users: number;
  crashedUsers: number;
}

export interface ReleaseHealthResponse {
  windowMinutes: number;
  versions: ReleaseHealthVersion[];
}

export interface ReleaseHealthTrendPoint {
  bucketUnixMs: number;
  version: string;
  sessions: number;
  crashedSessions: number;
}

export interface ReleaseHealthTrendResponse {
  windowMinutes: number;
  bucketMinutes: number;
  points: ReleaseHealthTrendPoint[];
}

export interface ExceptionGroup {
  exceptionType: string;
  exceptionMessage: string;
  occurrenceCount: number;
  firstSeen: string;
  lastSeen: string;
  affectedServices: string[];
  samplePaths: string[];
  owner: string;
}

export interface ExceptionGroupsResponse {
  groups: ExceptionGroup[];
}

export interface ExceptionFacetValuesResponse {
  values: Array<{ value: string; count: number }>;
}

export interface ErrorIssue {
  id: string;
  exceptionType: string;
  exceptionMessage: string;
  status: 'Open' | 'Resolved' | 'Ignored' | 'Regressed';
  assignee: string;
  statusChangedAt: string;
  issueUrl: string;
  occurrencesSinceChange: number;
  regressedVersion: string;
  updatedAt: string;
}

export interface ErrorIssueListResponse {
  issues: ErrorIssue[];
}

export interface Release {
  id: string;
  service: string;
  version: string;
  commit: string;
  url: string;
  notes: string;
  deployedAt: string;
  createdBy: string;
  newErrorCount?: number | null;
  regressionCount?: number | null;
}

export interface ReleaseListResponse {
  releases: Release[];
}
