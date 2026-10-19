import { DataFrame, DataLink, FieldType, createDataFrame } from '@grafana/data';

import {
  AppSessionsResponse,
  ErrorIssueListResponse,
  ExceptionGroupsResponse,
  ReleaseHealthResponse,
  ReleaseHealthTrendResponse,
  ReleaseListResponse,
  TrendMetric,
} from './types';

const NO_MATCH = '(none)';

function notices(...texts: Array<string | false | undefined>) {
  const list = texts.filter((t): t is string => !!t).map((text) => ({ severity: 'warning' as const, text }));
  return list.length > 0 ? { notices: list } : undefined;
}

function link(dashboardUrl: string | undefined, path: string, title: string): DataLink[] | undefined {
  const base = dashboardUrl?.trim().replace(/\/+$/, '');
  return base ? [{ title, url: `${base}${path}`, targetBlank: true }] : undefined;
}

/** Percent of `total` that did not `bad`; null when there was nothing to measure. */
export function crashFree(total: number, bad: number): number | null {
  return total > 0 ? (1 - bad / total) * 100 : null;
}

export function sessionsFrame(
  refId: string,
  response: AppSessionsResponse,
  extra: { clamped?: boolean; dashboardUrl?: string } = {}
): DataFrame {
  const rows = response.sessions;
  return createDataFrame({
    refId,
    name: 'Sessions',
    meta: notices(
      response.truncated && 'Flare returned only the most recent sessions; narrow the service, version or time range.',
      extra.clamped && 'Flare answers at most 7 days; the range was cut to its last 7 days.'
    ),
    fields: [
      { name: 'firstSeen', type: FieldType.time, values: rows.map((r) => r.firstSeenUnixMs), config: { displayName: 'Started' } },
      { name: 'lastSeen', type: FieldType.time, values: rows.map((r) => r.lastSeenUnixMs), config: { displayName: 'Last seen' } },
      {
        name: 'sessionId',
        type: FieldType.string,
        values: rows.map((r) => r.sessionId),
        config: { displayName: 'Session', links: link(extra.dashboardUrl, '/sessions/${__value.raw}', 'Open in Flare') },
      },
      { name: 'service', type: FieldType.string, values: rows.map((r) => r.serviceName), config: { displayName: 'Service' } },
      { name: 'version', type: FieldType.string, values: rows.map((r) => r.version), config: { displayName: 'Version' } },
      { name: 'os', type: FieldType.string, values: rows.map((r) => r.os), config: { displayName: 'OS' } },
      { name: 'device', type: FieldType.string, values: rows.map((r) => r.device), config: { displayName: 'Device' } },
      { name: 'spans', type: FieldType.number, values: rows.map((r) => r.spanCount), config: { displayName: 'Spans' } },
      { name: 'traces', type: FieldType.number, values: rows.map((r) => r.traceCount), config: { displayName: 'Traces' } },
      { name: 'errors', type: FieldType.number, values: rows.map((r) => r.errorCount), config: { displayName: 'Errors' } },
      { name: 'screens', type: FieldType.string, values: rows.map((r) => r.screens.join(', ')), config: { displayName: 'Screens' } },
    ],
  });
}

export function errorGroupsFrame(refId: string, response: ExceptionGroupsResponse, dashboardUrl?: string): DataFrame {
  const rows = response.groups;
  return createDataFrame({
    refId,
    name: 'Errors',
    fields: [
      {
        name: 'exceptionType',
        type: FieldType.string,
        values: rows.map((r) => r.exceptionType),
        config: { displayName: 'Type', links: link(dashboardUrl, '/errors', 'Open errors in Flare') },
      },
      { name: 'message', type: FieldType.string, values: rows.map((r) => r.exceptionMessage), config: { displayName: 'Message' } },
      { name: 'occurrences', type: FieldType.number, values: rows.map((r) => r.occurrenceCount), config: { displayName: 'Occurrences' } },
      { name: 'firstSeen', type: FieldType.time, values: rows.map((r) => Date.parse(r.firstSeen)), config: { displayName: 'First seen' } },
      { name: 'lastSeen', type: FieldType.time, values: rows.map((r) => Date.parse(r.lastSeen)), config: { displayName: 'Last seen' } },
      { name: 'services', type: FieldType.string, values: rows.map((r) => r.affectedServices.join(', ')), config: { displayName: 'Services' } },
      { name: 'owner', type: FieldType.string, values: rows.map((r) => r.owner), config: { displayName: 'Owner' } },
    ],
  });
}

export function errorIssuesFrame(refId: string, response: ErrorIssueListResponse, dashboardUrl?: string): DataFrame {
  const rows = response.issues;
  return createDataFrame({
    refId,
    name: 'Error issues',
    fields: [
      {
        name: 'exceptionType',
        type: FieldType.string,
        values: rows.map((r) => r.exceptionType),
        config: { displayName: 'Type', links: link(dashboardUrl, '/errors', 'Open errors in Flare') },
      },
      { name: 'message', type: FieldType.string, values: rows.map((r) => r.exceptionMessage), config: { displayName: 'Message' } },
      { name: 'status', type: FieldType.string, values: rows.map((r) => r.status), config: { displayName: 'Status' } },
      { name: 'assignee', type: FieldType.string, values: rows.map((r) => r.assignee), config: { displayName: 'Assignee' } },
      { name: 'regressedVersion', type: FieldType.string, values: rows.map((r) => r.regressedVersion), config: { displayName: 'Regressed in' } },
      { name: 'occurrences', type: FieldType.number, values: rows.map((r) => r.occurrencesSinceChange), config: { displayName: 'Since status change' } },
      { name: 'issueUrl', type: FieldType.string, values: rows.map((r) => r.issueUrl), config: { displayName: 'Issue', links: [{ title: 'Open issue', url: '${__value.raw}', targetBlank: true }] } },
      { name: 'statusChanged', type: FieldType.time, values: rows.map((r) => Date.parse(r.statusChangedAt)), config: { displayName: 'Status changed' } },
      { name: 'updated', type: FieldType.time, values: rows.map((r) => Date.parse(r.updatedAt)), config: { displayName: 'Updated' } },
    ],
  });
}

/** One row per app version in the window, with the crash-free rates Flare's Releases view shows. */
export function releaseHealthFrame(
  refId: string,
  responses: Array<{ service?: string; response: ReleaseHealthResponse }>,
  extra: { clamped?: boolean } = {}
): DataFrame {
  const rows = responses.flatMap(({ service, response }) => response.versions.map((v) => ({ service: service ?? '', v })));
  const percent = { unit: 'percent', decimals: 2 };
  return createDataFrame({
    refId,
    name: 'Release health',
    meta: notices(extra.clamped && 'Flare answers at most 7 days; the range was cut to its last 7 days.'),
    fields: [
      ...(responses.some((r) => r.service)
        ? [{ name: 'service', type: FieldType.string, values: rows.map((r) => r.service), config: { displayName: 'Service' } }]
        : []),
      { name: 'version', type: FieldType.string, values: rows.map((r) => r.v.version || NO_MATCH), config: { displayName: 'Version' } },
      { name: 'sessions', type: FieldType.number, values: rows.map((r) => r.v.sessions), config: { displayName: 'Sessions' } },
      { name: 'crashedSessions', type: FieldType.number, values: rows.map((r) => r.v.crashedSessions), config: { displayName: 'Crashed sessions' } },
      { name: 'erroredSessions', type: FieldType.number, values: rows.map((r) => r.v.erroredSessions), config: { displayName: 'Errored sessions' } },
      { name: 'crashFreeSessions', type: FieldType.number, values: rows.map((r) => crashFree(r.v.sessions, r.v.crashedSessions)), config: { ...percent, displayName: 'Crash-free sessions' } },
      { name: 'users', type: FieldType.number, values: rows.map((r) => r.v.users), config: { displayName: 'Users' } },
      { name: 'crashedUsers', type: FieldType.number, values: rows.map((r) => r.v.crashedUsers), config: { displayName: 'Crashed users' } },
      { name: 'crashFreeUsers', type: FieldType.number, values: rows.map((r) => crashFree(r.v.users, r.v.crashedUsers)), config: { ...percent, displayName: 'Crash-free users' } },
    ],
  });
}

const TREND_LABEL: Record<TrendMetric, string> = {
  crashFreeSessions: 'Crash-free sessions',
  sessions: 'Sessions',
  crashedSessions: 'Crashed sessions',
};

/** One time series frame per version (and per service when several were asked for), ready to draw. */
export function releaseHealthTrendFrames(
  refId: string,
  responses: Array<{ service?: string; response: ReleaseHealthTrendResponse }>,
  metric: TrendMetric,
  extra: { clamped?: boolean } = {}
): DataFrame[] {
  const frames: DataFrame[] = [];
  for (const { service, response } of responses) {
    const byVersion = new Map<string, typeof response.points>();
    for (const point of response.points) {
      const list = byVersion.get(point.version) ?? [];
      list.push(point);
      byVersion.set(point.version, list);
    }

    for (const [version, points] of [...byVersion].sort(([a], [b]) => a.localeCompare(b))) {
      points.sort((a, b) => a.bucketUnixMs - b.bucketUnixMs);
      const values = points.map((p) =>
        metric === 'crashFreeSessions' ? crashFree(p.sessions, p.crashedSessions) : metric === 'sessions' ? p.sessions : p.crashedSessions
      );
      const labels: Record<string, string> = { version: version || NO_MATCH };
      if (service) {
        labels.service = service;
      }
      frames.push(
        createDataFrame({
          refId,
          meta: notices(extra.clamped && 'Flare answers at most 7 days; the range was cut to its last 7 days.'),
          fields: [
            { name: 'Time', type: FieldType.time, values: points.map((p) => p.bucketUnixMs) },
            {
              name: TREND_LABEL[metric],
              type: FieldType.number,
              values,
              labels,
              config: {
                // The legend reads "2.1.0" (or "app 2.1.0"), not "Crash-free sessions {version=...}": the panel title says what is drawn.
                displayNameFromDS: [service, version || NO_MATCH].filter(Boolean).join(' '),
                ...(metric === 'crashFreeSessions' ? { unit: 'percent', decimals: 2, min: 0, max: 100 } : { decimals: 0 }),
              },
            },
          ],
        })
      );
    }
  }
  return frames;
}

/**
 * Deploys as a time-keyed table. Used as an annotation query it needs only its `time`, `title` and `text`
 * fields mapped in Grafana's annotation settings.
 */
export function releasesFrame(refId: string, response: ReleaseListResponse): DataFrame {
  const rows = response.releases;
  return createDataFrame({
    refId,
    name: 'Releases',
    fields: [
      { name: 'time', type: FieldType.time, values: rows.map((r) => Date.parse(r.deployedAt)), config: { displayName: 'Deployed' } },
      { name: 'service', type: FieldType.string, values: rows.map((r) => r.service), config: { displayName: 'Service' } },
      { name: 'version', type: FieldType.string, values: rows.map((r) => r.version), config: { displayName: 'Version' } },
      { name: 'title', type: FieldType.string, values: rows.map((r) => `${r.service} ${r.version}`), config: { displayName: 'Title' } },
      { name: 'text', type: FieldType.string, values: rows.map((r) => r.notes), config: { displayName: 'Notes' } },
      { name: 'commit', type: FieldType.string, values: rows.map((r) => r.commit), config: { displayName: 'Commit' } },
      { name: 'url', type: FieldType.string, values: rows.map((r) => r.url), config: { displayName: 'URL', links: [{ title: 'Open', url: '${__value.raw}', targetBlank: true }] } },
      { name: 'newErrors', type: FieldType.number, values: rows.map((r) => r.newErrorCount ?? null), config: { displayName: 'New errors' } },
      { name: 'regressions', type: FieldType.number, values: rows.map((r) => r.regressionCount ?? null), config: { displayName: 'Regressions' } },
    ],
  });
}
