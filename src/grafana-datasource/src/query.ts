import { FlareQuery } from './types';

/** The dashboard's time range, in Unix milliseconds. */
export interface TimeWindow {
  fromMs: number;
  toMs: number;
}

// Flare.Api's session endpoints take a window ending at `endUnixMs`, clamped server-side to this range.
export const MIN_WINDOW_MINUTES = 5;
export const MAX_WINDOW_MINUTES = 10080;

export interface ResolvedWindow {
  windowMinutes: number;
  endUnixMs: number;
  /** The dashboard range was wider than Flare answers, so the window ends where the range does but starts later. */
  clamped: boolean;
}

export function resolveWindow(window: TimeWindow): ResolvedWindow {
  const requested = Math.ceil((window.toMs - window.fromMs) / 60000);
  const windowMinutes = Math.min(MAX_WINDOW_MINUTES, Math.max(MIN_WINDOW_MINUTES, requested));
  return { windowMinutes, endUnixMs: window.toMs, clamped: requested > MAX_WINDOW_MINUTES };
}

/** A comma separated service field ("a,b", also what a multi-value variable expands to) as a clean list. */
export function splitServices(raw: string | undefined): string[] {
  return (raw ?? '')
    .replace(/^\{|\}$/g, '')
    .split(',')
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}

/**
 * The services a single-service endpoint is called for. No selection is one call for every service (`undefined`),
 * several are one call each, because those endpoints take a single service.
 */
export function serviceCalls(raw: string | undefined): Array<string | undefined> {
  const services = splitServices(raw);
  return services.length === 0 ? [undefined] : services;
}

export function sessionsRequest(query: FlareQuery, window: ResolvedWindow, service: string | undefined) {
  return {
    windowMinutes: window.windowMinutes,
    endUnixMs: window.endUnixMs,
    ...(service ? { service } : {}),
    ...(query.version?.trim() ? { version: query.version.trim() } : {}),
    errorsOnly: query.errorsOnly === true,
  };
}

export function releaseHealthRequest(window: ResolvedWindow, service: string | undefined) {
  return {
    windowMinutes: window.windowMinutes,
    endUnixMs: window.endUnixMs,
    ...(service ? { service } : {}),
  };
}

/** `bucketMinutes` from the query, else the dashboard interval rounded up to whole minutes; Flare falls back to a nice width if it is too fine. */
export function trendBucketMinutes(query: FlareQuery, intervalMs: number | undefined): number | undefined {
  if (query.bucketMinutes && query.bucketMinutes > 0) {
    return Math.floor(query.bucketMinutes);
  }
  return intervalMs && intervalMs > 0 ? Math.max(1, Math.ceil(intervalMs / 60000)) : undefined;
}

export function releaseHealthTrendRequest(
  query: FlareQuery,
  window: ResolvedWindow,
  service: string | undefined,
  intervalMs: number | undefined
) {
  const bucketMinutes = trendBucketMinutes(query, intervalMs);
  return {
    ...releaseHealthRequest(window, service),
    ...(bucketMinutes ? { bucketMinutes } : {}),
  };
}

export function errorsRequest(query: FlareQuery, window: TimeWindow) {
  const services = splitServices(query.service);
  return {
    filter: {
      from: new Date(window.fromMs).toISOString(),
      to: new Date(window.toMs).toISOString(),
      ...(services.length > 0 ? { services } : {}),
    },
    ...(query.topN && query.topN > 0 ? { topN: Math.floor(query.topN) } : {}),
  };
}
