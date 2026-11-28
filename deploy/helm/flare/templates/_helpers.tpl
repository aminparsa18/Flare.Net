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

{{/* "true" when the bundled ClickHouse runs with the least-privilege users of files/flare-users.xml (ADR-0282). */}}
{{- define "flare.clickhouseUsersOn" -}}{{ if and .Values.clickhouse.enabled .Values.clickhouse.users.enabled }}true{{ end }}{{- end -}}

{{/*
Env vars every .NET component that talks to ClickHouse and Redis needs.
Call with dict "root" . "component" "ingest" | "api" | "alert-worker".
ConnectionStrings__clickhousedb (`default`) is the migration runner's DDL credential; each per-user connection
string falls back to it when unset. alert-worker runs no migrations but still gets it (ADR-0283): AlertWorker
images older than ADR-0282 read only `clickhousedb`, and an image pinned below the chart's must not crash-loop.
Newer images prefer clickhousedb-app, so they still run as flare_app.
In job mode (flare.migrationJobOn) no component gets CLICKHOUSE_PASSWORD or clickhousedb (ADR-0284): the Job
runs the migrations, and job mode is only on for an image with the `migrate` command (ADR-0285), so the old-image
fallback above doesn't apply.
*/}}
{{/*
Why the one-shot migration Job (ADR-0284) is off even though clickhouse.migrationJob.enabled is true, or empty when
it isn't (ADR-0285). The chart then falls back to migrating at ingest/api startup, as before ADR-0284:
- no per-user connection strings (users.enabled off, or an external ClickHouse without usersSecret): every service
  connects as `default` anyway, so the Job would take no credential away;
- the image tag is a release older than 0.7.0, whose api image has no `migrate` command. edge, sha-... and any tag
  that isn't a version number count as new.
*/}}
{{- define "flare.migrationJobFallbackReason" -}}
{{- if .Values.clickhouse.migrationJob.enabled -}}
{{- $tag := toString (.Values.image.tag | default .Chart.AppVersion) | trimPrefix "v" -}}
{{- $oldImage := false -}}
{{- if regexMatch "^[0-9]+\\.[0-9]+(\\.[0-9]+)?([-+].*)?$" $tag -}}
{{- $parts := splitList "." (regexReplaceAll "[-+].*$" $tag "") -}}
{{- $oldImage = and (eq (index $parts 0) "0") (lt (atoi (index $parts 1)) 7) -}}
{{- end -}}
{{- if not (or (include "flare.clickhouseUsersOn" .) (and (not .Values.clickhouse.enabled) .Values.clickhouse.external.usersSecret)) -}}
no per-user ClickHouse connection strings (clickhouse.users.enabled is off, or an external ClickHouse has no clickhouse.external.usersSecret), so every service connects as `default` anyway
{{- else if $oldImage -}}
image tag {{ $tag }} is older than 0.7.0, whose api image has no `migrate` command
{{- end -}}
{{- end -}}
{{- end -}}

{{/* "true" when migrations run in the one-shot Job (ADR-0284, on by default since ADR-0285). */}}
{{- define "flare.migrationJobOn" -}}
{{- if and .Values.clickhouse.migrationJob.enabled (not (include "flare.migrationJobFallbackReason" .)) -}}true{{- end -}}
{{- end -}}

{{- define "flare.backingEnv" -}}
{{- $root := .root -}}
{{- $usersOn := eq (include "flare.clickhouseUsersOn" $root) "true" -}}
{{- $chPrefix := printf "Host=%s;Port=%s;Database=clickhousedb" (include "flare.clickhouseHost" $root) (include "flare.clickhousePort" $root) -}}
{{- $jobOn := eq (include "flare.migrationJobOn" $root) "true" -}}
{{- with $root -}}
{{- if not $jobOn }}
- name: CLICKHOUSE_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "flare.clickhouseSecret" . }}
      key: clickhouse-password
{{- end }}
- name: REDIS_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "flare.redisSecret" . }}
      key: redis-password
{{- if not $jobOn }}
- name: ConnectionStrings__clickhousedb
  value: "{{ $chPrefix }};Username={{ include "flare.clickhouseUser" . }};Password=$(CLICKHOUSE_PASSWORD)"
{{- end }}
{{- range $conn := (eq $.component "ingest" | ternary (list "ingest") (eq $.component "api" | ternary (list "query" "app") (list "app"))) }}
{{- if $usersOn }}
- name: FLARE_CH_{{ upper $conn }}_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "flare.secretName" $root }}
      key: clickhouse-{{ $conn }}-password
- name: ConnectionStrings__clickhousedb-{{ $conn }}
  value: "{{ $chPrefix }};Username=flare_{{ $conn }};Password=$(FLARE_CH_{{ upper $conn }}_PASSWORD)"
{{- else if and (not $root.Values.clickhouse.enabled) $root.Values.clickhouse.external.usersSecret }}
- name: ConnectionStrings__clickhousedb-{{ $conn }}
  valueFrom:
    secretKeyRef:
      name: {{ $root.Values.clickhouse.external.usersSecret }}
      key: clickhousedb-{{ $conn }}
      {{- /* Job mode has no clickhousedb to fall back to, so a missing key must fail the pod. */}}
      optional: {{ not $jobOn }}
{{- end }}
{{- end }}
- name: ConnectionStrings__redis
  value: "{{ include "flare.redisHost" . }}:{{ include "flare.redisPort" . }},password=$(REDIS_PASSWORD)"
{{- if and (not .Values.clickhouse.enabled) .Values.clickhouse.external.clusterMode }}
- name: ClickHouse__ClusterMode
  value: "true"
{{- end }}
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
