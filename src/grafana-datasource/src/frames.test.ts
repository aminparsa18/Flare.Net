import { FieldType } from '@grafana/data';

import { crashFree, errorGroupsFrame, releaseHealthFrame, releaseHealthTrendFrames, releasesFrame, sessionsFrame } from './frames';
import { AppSessionsResponse } from './types';

const sessions: AppSessionsResponse = {
  windowMinutes: 60,
  truncated: true,
  services: [],
  versions: [],
  sessions: [
    {
      sessionId: 's1',
      serviceName: 'app',
      version: '2.0',
      os: 'iOS',
      device: 'iPhone',
      firstSeenUnixMs: 1000,
      lastSeenUnixMs: 2000,
      spanCount: 5,
      traceCount: 2,
      errorCount: 1,
      screens: ['Home', 'Cart'],
    },
  ],
};

describe('crashFree', () => {
  it('is the share of sessions that did not crash, null with no sessions', () => {
    expect(crashFree(200, 2)).toBeCloseTo(99);
    expect(crashFree(0, 0)).toBeNull();
  });
});

describe('sessionsFrame', () => {
  it('maps a session to a row with a time first field and joined screens', () => {
    const frame = sessionsFrame('A', sessions);
    expect(frame.length).toBe(1);
    expect(frame.fields[0].type).toBe(FieldType.time);
    expect(frame.fields.find((f) => f.name === 'screens')?.values[0]).toBe('Home, Cart');
  });

  it('warns when Flare cut the list or the range', () => {
    expect(sessionsFrame('A', sessions, { clamped: true }).meta?.notices).toHaveLength(2);
    expect(sessionsFrame('A', { ...sessions, truncated: false }).meta?.notices).toBeUndefined();
  });

  it('links a session to the Flare dashboard only when its URL is configured', () => {
    const idField = (url?: string) => sessionsFrame('A', sessions, { dashboardUrl: url }).fields.find((f) => f.name === 'sessionId')!;
    expect(idField().config.links).toBeUndefined();
    expect(idField('http://flare:5173/').config.links?.[0].url).toBe('http://flare:5173/sessions/${__value.raw}');
  });
});

describe('errorGroupsFrame', () => {
  it('parses ISO timestamps to epoch milliseconds', () => {
    const frame = errorGroupsFrame('A', {
      groups: [
        {
          exceptionType: 'IOException',
          exceptionMessage: 'boom',
          occurrenceCount: 3,
          firstSeen: '2026-10-19T10:00:00Z',
          lastSeen: '2026-10-19T11:00:00Z',
          affectedServices: ['a', 'b'],
          samplePaths: [],
          owner: '',
        },
      ],
    });
    expect(frame.fields.find((f) => f.name === 'firstSeen')?.values[0]).toBe(Date.parse('2026-10-19T10:00:00Z'));
    expect(frame.fields.find((f) => f.name === 'services')?.values[0]).toBe('a, b');
  });
});

describe('releaseHealthFrame', () => {
  const response = {
    windowMinutes: 60,
    versions: [{ version: '2.0', sessions: 100, crashedSessions: 1, erroredSessions: 5, users: 0, crashedUsers: 0 }],
  };

  it('computes crash-free sessions and leaves users null when nobody was identified', () => {
    const frame = releaseHealthFrame('A', [{ response }]);
    expect(frame.fields.find((f) => f.name === 'crashFreeSessions')?.values[0]).toBeCloseTo(99);
    expect(frame.fields.find((f) => f.name === 'crashFreeUsers')?.values[0]).toBeNull();
    expect(frame.fields.some((f) => f.name === 'service')).toBe(false);
  });

  it('adds a service column when the query named services', () => {
    const frame = releaseHealthFrame('A', [{ service: 'app', response }]);
    expect(frame.fields[0].name).toBe('service');
  });
});

describe('releaseHealthTrendFrames', () => {
  const response = {
    windowMinutes: 60,
    bucketMinutes: 5,
    points: [
      { bucketUnixMs: 2000, version: '2.0', sessions: 10, crashedSessions: 1 },
      { bucketUnixMs: 1000, version: '2.0', sessions: 10, crashedSessions: 0 },
      { bucketUnixMs: 1000, version: '1.0', sessions: 4, crashedSessions: 0 },
    ],
  };

  it('is one time-ordered frame per version, labelled by version', () => {
    const frames = releaseHealthTrendFrames('A', [{ response }], 'crashFreeSessions');
    expect(frames.map((f) => f.fields[1].config.displayNameFromDS)).toEqual(['1.0', '2.0']);
    expect(frames[1].fields[0].values).toEqual([1000, 2000]);
    expect(frames[1].fields[1].values).toEqual([100, 90]);
    expect(frames[1].fields[1].labels).toEqual({ version: '2.0' });
  });

  it('draws raw counts for the sessions series and labels the service when given', () => {
    const frames = releaseHealthTrendFrames('A', [{ service: 'app', response }], 'crashedSessions');
    expect(frames[1].fields[1].values).toEqual([0, 1]);
    expect(frames[1].fields[1].labels).toEqual({ version: '2.0', service: 'app' });
  });
});

describe('releasesFrame', () => {
  it('exposes time, title and text fields for annotation mapping', () => {
    const frame = releasesFrame('A', {
      releases: [{ id: '1', service: 'app', version: '2.0', commit: 'abc', url: '', notes: 'hotfix', deployedAt: '2026-10-19T10:00:00Z', createdBy: '' }],
    });
    expect(frame.fields.find((f) => f.name === 'time')?.values[0]).toBe(Date.parse('2026-10-19T10:00:00Z'));
    expect(frame.fields.find((f) => f.name === 'title')?.values[0]).toBe('app 2.0');
    expect(frame.fields.find((f) => f.name === 'text')?.values[0]).toBe('hotfix');
    expect(frame.fields.find((f) => f.name === 'newErrors')?.values[0]).toBeNull();
  });
});
