import React from 'react';
import { QueryEditorProps } from '@grafana/data';
import { Combobox, ComboboxOption, InlineField, InlineFieldRow, Input } from '@grafana/ui';

import { DataSource } from '../datasource';
import { FlareDataSourceOptions, FlareQuery, FlareVariableQuery, VariableKind } from '../types';

type Props = QueryEditorProps<DataSource, FlareQuery, FlareDataSourceOptions, FlareVariableQuery>;

const KINDS: Array<ComboboxOption<VariableKind>> = [
  { value: 'sessionServices', label: 'Services with sessions' },
  { value: 'sessionVersions', label: 'App versions with sessions' },
  { value: 'errorServices', label: 'Services with errors' },
  { value: 'releaseVersions', label: 'Release versions' },
];

export function VariableQueryEditor({ query, onChange }: Props) {
  const kind = query.kind ?? 'sessionServices';
  const needsService = kind === 'sessionVersions' || kind === 'releaseVersions';

  return (
    <InlineFieldRow>
      <InlineField label="Values" labelWidth={14}>
        <Combobox width={32} options={KINDS} value={kind} onChange={(v) => onChange({ ...query, kind: v.value })} />
      </InlineField>
      {needsService && (
        <InlineField label="Service" labelWidth={10} tooltip="Narrow the versions to one service; a variable works. Empty lists every service's.">
          <Input
            width={30}
            value={query.service ?? ''}
            placeholder="all services"
            onChange={(e) => onChange({ ...query, kind, service: e.currentTarget.value })}
          />
        </InlineField>
      )}
    </InlineFieldRow>
  );
}
