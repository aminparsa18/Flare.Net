import {
  errorsRequest,
  releaseHealthTrendRequest,
  resolveWindow,
  serviceCalls,
  sessionsRequest,
  splitServices,
  trendBucketMinutes,
} from './query';
import { FlareQuery } from './types';

const query = (patch: Partial<FlareQuery> = {}): FlareQuery => ({ refId: 'A', kind: 'sessions', ...patch });

describe('resolveWindow', () => {
  it('rounds the range up to whole minutes and ends where the range ends', () => {
    expect(resolveWindow({ fromMs: 0, toMs: 90 * 60000 + 1 })).toEqual({ windowMinutes: 91, endUnixMs: 90 * 60000 + 1, clamped: false });
  });

  it('raises a tiny range to the 5 minute minimum', () => {
    expect(resolveWindow({ fromMs: 1000, toMs: 2000 }).windowMinutes).toBe(5);
  });

  it('cuts a range over 7 days and says so', () => {
    const w = resolveWindow({ fromMs: 0, toMs: 30 * 24 * 3600_000 });
    expect(w.windowMinutes).toBe(10080);
    expect(w.clamped).toBe(true);
  });
});

describe('splitServices / serviceCalls', () => {
  it('splits a comma list and a multi-value variable expansion', () => {
    expect(splitServices('api, web')).toEqual(['api', 'web']);
    expect(splitServices('{api,web}')).toEqual(['api', 'web']);
  });

  it('is one unscoped call when nothing is selected, one call per service otherwise', () => {
    expect(serviceCalls('')).toEqual([undefined]);
    expect(serviceCalls(undefined)).toEqual([undefined]);
    expect(serviceCalls('api,web')).toEqual(['api', 'web']);
  });
});

describe('request builders', () => {
  const window = resolveWindow({ fromMs: 0, toMs: 3600_000 });

  it('sessions request carries the window, optional service and version, and errorsOnly', () => {
    expect(sessionsRequest(query({ version: ' 2.0 ', errorsOnly: true }), window, 'api')).toEqual({
      windowMinutes: 60,
      endUnixMs: 3600_000,
      service: 'api',
      version: '2.0',
      errorsOnly: true,
    });
    expect(sessionsRequest(query(), window, undefined)).toEqual({ windowMinutes: 60, endUnixMs: 3600_000, errorsOnly: false });
  });

  it('errors request sends ISO bounds, the services list and topN only when set', () => {
    expect(errorsRequest(query({ kind: 'errors', service: 'api,web', topN: 5 }), { fromMs: 0, toMs: 1000 })).toEqual({
      filter: { from: '1970-01-01T00:00:00.000Z', to: '1970-01-01T00:00:01.000Z', services: ['api', 'web'] },
      topN: 5,
    });
    expect(errorsRequest(query({ kind: 'errors' }), { fromMs: 0, toMs: 1000 }).filter).not.toHaveProperty('services');
  });

  it('trend bucket: the query wins, else the panel interval in whole minutes, else left to Flare', () => {
    expect(trendBucketMinutes(query({ bucketMinutes: 15.9 }), 60000)).toBe(15);
    expect(trendBucketMinutes(query(), 90_000)).toBe(2);
    expect(trendBucketMinutes(query(), 1)).toBe(1);
    expect(trendBucketMinutes(query(), undefined)).toBeUndefined();
    expect(releaseHealthTrendRequest(query(), window, 'api', undefined)).not.toHaveProperty('bucketMinutes');
  });
});
