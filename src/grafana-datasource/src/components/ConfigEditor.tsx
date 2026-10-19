import React, { ChangeEvent } from 'react';
import { DataSourcePluginOptionsEditorProps } from '@grafana/data';
import { Alert, Field, Input, SecretInput } from '@grafana/ui';

import { FlareDataSourceOptions, FlareSecureJsonData } from '../types';

type Props = DataSourcePluginOptionsEditorProps<FlareDataSourceOptions, FlareSecureJsonData>;

export function ConfigEditor({ options, onOptionsChange }: Props) {
  const { jsonData, secureJsonFields, secureJsonData } = options;

  const onJsonChange = (key: keyof FlareDataSourceOptions) => (event: ChangeEvent<HTMLInputElement>) =>
    onOptionsChange({ ...options, jsonData: { ...jsonData, [key]: event.target.value } });

  const onTokenChange = (event: ChangeEvent<HTMLInputElement>) =>
    onOptionsChange({ ...options, secureJsonData: { token: event.target.value } });

  const onTokenReset = () =>
    onOptionsChange({
      ...options,
      secureJsonFields: { ...secureJsonFields, token: false },
      secureJsonData: { ...secureJsonData, token: '' },
    });

  return (
    <>
      <Alert severity="info" title="Sessions, errors and release health">
        This data source reads what Flare&apos;s Prometheus, Loki and Tempo compatible APIs cannot express. Add those as ordinary
        Prometheus, Loki and Tempo data sources for metrics, logs and traces.
      </Alert>

      <Field label="Flare API URL" description="The Flare.Api base URL, as the Grafana server reaches it (not the browser)." required>
        <Input
          width={50}
          value={jsonData.url ?? ''}
          placeholder="http://localhost:8080"
          onChange={onJsonChange('url')}
          data-testid="flare-url"
        />
      </Field>

      <Field
        label="Personal access token"
        description="A flr_pat_ token (Settings > Access tokens). Leave empty only if Flare has authentication turned off."
      >
        <SecretInput
          width={50}
          isConfigured={Boolean(secureJsonFields?.token)}
          value={secureJsonData?.token ?? ''}
          placeholder="flr_pat_..."
          onReset={onTokenReset}
          onChange={onTokenChange}
          data-testid="flare-token"
        />
      </Field>

      <Field
        label="Flare dashboard URL"
        description="Optional. When set, session rows link to their page in the Flare dashboard."
      >
        <Input
          width={50}
          value={jsonData.dashboardUrl ?? ''}
          placeholder="http://localhost:5173"
          onChange={onJsonChange('dashboardUrl')}
        />
      </Field>
    </>
  );
}
