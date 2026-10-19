import React, { ChangeEvent } from 'react';
import { QueryEditorProps } from '@grafana/data';
import { ComboboxOption, Combobox, InlineField, InlineFieldRow, InlineSwitch, Input } from '@grafana/ui';

import { DataSource } from '../datasource';
import { FlareDataSourceOptions, FlareQuery, QueryKind, TrendMetric } from '../types';

type Props = QueryEditorProps<DataSource, FlareQuery, FlareDataSourceOptions>;

const KINDS: Array<ComboboxOption<QueryKind>> = [
  { value: 'sessions', label: 'Sessions', description: 'Client app sessions in the range, one row each' },
  { value: 'errors', label: 'Errors', description: 'Exception groups ranked by occurrences' },
  { value: 'issues', label: 'Error issues', description: 'Triage state of the error groups: status, assignee, regressions' },
  { value: 'releaseHealth', label: 'Release health', description: 'Crash-free sessions and users per app version' },
  { value: 'releaseHealthTrend', label: 'Release health trend', description: 'Crash-free sessions per version over time' },
  { value: 'releases', label: 'Releases', description: 'Marked deploys; also usable as annotations' },
];

const TREND_METRICS: Array<ComboboxOption<TrendMetric>> = [
  { value: 'crashFreeSessions', label: 'Crash-free sessions %' },
  { value: 'sessions', label: 'Sessions' },
  { value: 'crashedSessions', label: 'Crashed sessions' },
];

const SERVICE_KINDS: QueryKind[] = ['sessions', 'errors', 'releaseHealth', 'releaseHealthTrend', 'releases'];

export function QueryEditor({ query, onChange, onRunQuery }: Props) {
  const kind = query.kind ?? 'sessions';
  const update = (patch: Partial<FlareQuery>) => {
    onChange({ ...query, ...patch });
    onRunQuery();
  };
  const text = (key: 'service' | 'version') => (event: ChangeEvent<HTMLInputElement>) =>
    onChange({ ...query, [key]: event.target.value });
  const number = (key: 'topN' | 'bucketMinutes') => (event: ChangeEvent<HTMLInputElement>) =>
    onChange({ ...query, [key]: event.target.value ? Number(event.target.value) : undefined });

  return (
    <>
      <InlineFieldRow>
        <InlineField label="Query" labelWidth={14}>
          <Combobox width={30} options={KINDS} value={kind} onChange={(v) => update({ kind: v.value })} />
        </InlineField>
        {SERVICE_KINDS.includes(kind) && (
          <InlineField
            label="Service"
            labelWidth={10}
            tooltip="One or more service names, comma separated; a multi-value variable works. Empty means every service."
          >
            <Input width={30} value={query.service ?? ''} placeholder="all services" onChange={text('service')} onBlur={onRunQuery} />
          </InlineField>
        )}
      </InlineFieldRow>

      {kind === 'sessions' && (
        <InlineFieldRow>
          <InlineField label="Version" labelWidth={14}>
            <Input width={30} value={query.version ?? ''} placeholder="all versions" onChange={text('version')} onBlur={onRunQuery} />
          </InlineField>
          <InlineField label="Errors only" labelWidth={14} tooltip="Only sessions with at least one error">
            <InlineSwitch value={query.errorsOnly === true} onChange={(e) => update({ errorsOnly: e.currentTarget.checked })} />
          </InlineField>
        </InlineFieldRow>
      )}

      {kind === 'errors' && (
        <InlineFieldRow>
          <InlineField label="Top N" labelWidth={14} tooltip="How many exception groups to return">
            <Input width={12} type="number" min={1} value={query.topN ?? ''} placeholder="default" onChange={number('topN')} onBlur={onRunQuery} />
          </InlineField>
        </InlineFieldRow>
      )}

      {kind === 'releaseHealthTrend' && (
        <InlineFieldRow>
          <InlineField label="Series" labelWidth={14}>
            <Combobox
              width={30}
              options={TREND_METRICS}
              value={query.trendMetric ?? 'crashFreeSessions'}
              onChange={(v) => update({ trendMetric: v.value })}
            />
          </InlineField>
          <InlineField label="Bucket (min)" labelWidth={16} tooltip="Bucket width in minutes. Empty follows the panel interval.">
            <Input width={12} type="number" min={1} value={query.bucketMinutes ?? ''} placeholder="auto" onChange={number('bucketMinutes')} onBlur={onRunQuery} />
          </InlineField>
        </InlineFieldRow>
      )}
    </>
  );
}
