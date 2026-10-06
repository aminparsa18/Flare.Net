{{- define "flare.name" -}}{{ .Release.Name }}-flare{{- end -}}

{{- define "flare.labels" -}}
app.kubernetes.io/name: flare
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
{{- end -}}

{{- define "flare.selector" -}}
app.kubernetes.io/name: flare
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end -}}

{{/* flare.image: dict "root" . "name" "ingest" */}}
{{- define "flare.image" -}}
{{ .root.Values.image.registry }}/{{ .root.Values.image.repository }}/flare-{{ .name }}:{{ .root.Values.image.tag | default .root.Chart.AppVersion }}
{{- end -}}

{{- define "flare.basePath" -}}{{ .Values.basePath | default "" | trimSuffix "/" }}{{- end -}}

{{/* Where the browser loads the dashboard (origin, no base path). */}}
{{- define "flare.origin" -}}
{{- if .Values.publicUrl -}}{{ .Values.publicUrl | trimSuffix "/" }}{{- else -}}http://localhost:7777{{- end -}}
{{- end -}}

{{/* Dashboard URL incl. base path - what alert-notification deep links use. */}}
{{- define "flare.dashboardUrl" -}}{{ include "flare.origin" . }}{{ include "flare.basePath" . }}{{- end -}}

{{/* PUBLIC_API_URL: the API prefix as the browser sees it (the dashboard appends /api/...). */}}
{{- define "flare.publicApiUrl" -}}
{{- if .Values.publicUrl -}}{{ .Values.publicUrl | trimSuffix "/" }}{{ include "flare.basePath" . }}{{- else -}}http://localhost:8080{{- end -}}
{{- end -}}

{{/* Secret names. A user-supplied existingSecret wins, otherwise the chart's own. */}}
{{- define "flare.secretName" -}}{{ include "flare.name" . }}{{- end -}}
{{- define "flare.clickhouseSecret" -}}{{ .Values.clickhouse.existingSecret | default (include "flare.secretName" .) }}{{- end -}}
{{- define "flare.redisSecret" -}}{{ .Values.redis.existingSecret | default (include "flare.secretName" .) }}{{- end -}}
{{- define "flare.postgresSecret" -}}{{ .Values.identity.postgres.existingSecret | default (include "flare.secretName" .) }}{{- end -}}
{{- define "flare.smtpSecret" -}}{{ .Values.email.existingSecret | default (include "flare.secretName" .) }}{{- end -}}

{{- define "flare.clickhouseHost" -}}
{{- if .Values.clickhouse.enabled -}}{{ include "flare.name" . }}-clickhouse{{- else -}}{{ required "clickhouse.external.host is required when clickhouse.enabled=false" .Values.clickhouse.external.host }}{{- end -}}
{{- end -}}
{{- define "flare.clickhousePort" -}}{{ if .Values.clickhouse.enabled }}8123{{ else }}{{ .Values.clickhouse.external.port }}{{ end }}{{- end -}}
{{- define "flare.clickhouseUser" -}}{{ if .Values.clickhouse.enabled }}default{{ else }}{{ .Values.clickhouse.external.username }}{{ end }}{{- end -}}
{{- define "flare.redisHost" -}}
{{- if .Values.redis.enabled -}}{{ include "flare.name" . }}-redis{{- else -}}{{ required "redis.external.host is required when redis.enabled=false" .Values.redis.external.host }}{{- end -}}
{{- end -}}
{{- define "flare.redisPort" -}}{{ if .Values.redis.enabled }}6379{{ else }}{{ .Values.redis.external.port }}{{ end }}{{- end -}}

{{/*
Generated-once password: reuse what the live Secret already holds (so `helm upgrade` never
rotates a password out from under a persistent volume), else the value given, else random.
Call with dict "root" . "key" "redis-password" "value" .Values.redis.password
*/}}
{{- define "flare.password" -}}
{{- $existing := lookup "v1" "Secret" .root.Release.Namespace (include "flare.secretName" .root) -}}
{{- if .value -}}{{ .value }}
{{- else if and $existing (hasKey $existing.data .key) -}}{{ index $existing.data .key | b64dec }}
{{- else -}}{{ randAlphaNum 24 }}
{{- end -}}
{{- end -}}

{{/* Env vars every .NET component that talks to ClickHouse and Redis needs. */}}
{{- define "flare.backingEnv" -}}
- name: CLICKHOUSE_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "flare.clickhouseSecret" . }}
      key: clickhouse-password
- name: REDIS_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "flare.redisSecret" . }}
      key: redis-password
- name: ConnectionStrings__clickhousedb
  value: "Host={{ include "flare.clickhouseHost" . }};Port={{ include "flare.clickhousePort" . }};Database=clickhousedb;Username={{ include "flare.clickhouseUser" . }};Password=$(CLICKHOUSE_PASSWORD)"
- name: ConnectionStrings__redis
  value: "{{ include "flare.redisHost" . }}:{{ include "flare.redisPort" . }},password=$(REDIS_PASSWORD)"
{{- if and (not .Values.clickhouse.enabled) .Values.clickhouse.external.clusterMode }}
- name: ClickHouse__ClusterMode
  value: "true"
{{- end }}
{{- end -}}

{{- define "flare.smtpEnv" -}}
- name: Email__Host
  value: {{ .Values.email.host | quote }}
- name: Email__Port
  value: {{ .Values.email.port | quote }}
- name: Email__Username
  value: {{ .Values.email.username | quote }}
- name: Email__From
  value: {{ .Values.email.from | quote }}
- name: Email__UseStartTls
  value: {{ .Values.email.useStartTls | quote }}
- name: Email__Password
  valueFrom:
    secretKeyRef:
      name: {{ include "flare.smtpSecret" . }}
      key: smtp-password
{{- end -}}

{{/* Identity store env for ingest and api. */}}
{{- define "flare.identityEnv" -}}
{{- if eq .Values.identity.provider "Postgres" }}
- name: Identity__Provider
  value: Postgres
{{- if .Values.identity.postgres.enabled }}
- name: POSTGRES_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "flare.postgresSecret" . }}
      key: postgres-password
- name: Identity__ConnectionString
  value: "Host={{ include "flare.name" . }}-postgres;Database=flare_identity;Username=flare;Password=$(POSTGRES_PASSWORD)"
{{- else }}
- name: Identity__ConnectionString
  valueFrom:
    secretKeyRef:
      name: {{ .Values.identity.postgres.external.existingSecret | default (include "flare.secretName" .) }}
      key: identity-connection-string
{{- end }}
{{- else }}
- name: Identity__DbPath
  value: /data/identity/flare-identity.db
{{- end }}
{{- end -}}

{{/* Sqlite identity: mount + same-node affinity (only needed for ReadWriteOnce). */}}
{{- define "flare.identityMount" -}}
{{- if eq .Values.identity.provider "Sqlite" }}
volumeMounts:
  - name: identity
    mountPath: /data/identity
{{- end }}
{{- end -}}
{{- define "flare.identityVolume" -}}
{{- if eq .Values.identity.provider "Sqlite" }}
volumes:
  - name: identity
    persistentVolumeClaim:
      claimName: {{ include "flare.name" . }}-identity
{{- end }}
{{- end -}}
{{- define "flare.identityAffinity" -}}
{{- if and (eq .Values.identity.provider "Sqlite") (eq .Values.identity.sqlite.accessMode "ReadWriteOnce") }}
affinity:
  podAffinity:
    requiredDuringSchedulingIgnoredDuringExecution:
      - topologyKey: kubernetes.io/hostname
        labelSelector:
          matchLabels:
            app.kubernetes.io/instance: {{ .Release.Name }}
            flare.io/identity-store: shared
{{- end }}
{{- end -}}
