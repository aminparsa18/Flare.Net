# Flare threat model

Repository-scoped. Sensitive: it describes attack surface. Do not publish outside the maintainers.

Claim tags: **[obs]** observed in the code or an ADR cited next to it; **[assumed]** inferred or unverified, listed again under *Open questions*.

Existing material this builds on (not replaced): [authentication-model.md](../docs/explanation/authentication-model.md), ADR-0004, 0019, 0028, 0051, 0079, 0111, 0118, 0127, 0149, 0150, 0198, 0200, 0201, 0202, 0205, 0206, 0207. There is no `SECURITY.md` and no earlier threat model **[obs]**.

---

## Overview

Flare is a self-hosted, OpenTelemetry-native observability platform for .NET: logs, traces and metrics in ClickHouse, correlated, with alerting. The runtime pieces are:

| Component | Role | Listens on |
|---|---|---|
| `Flare.Ingest` | OTLP receiver (gRPC/HTTP) → Redis Streams → ClickHouse; also `/v1/feedback`, `/v1/screenshots` | 4317, 4318 |
| `Flare.Api` | Query/admin API, live-tail WebSocket, `/mcp`, auth flows, public status pages | 8080 |
| `Flare.AlertWorker` | Evaluates alert rules, sends notifications, runs synthetic probes | none public |
| `src/dashboard` | SvelteKit (adapter-node) SPA/SSR server, talks to Flare.Api cross-origin | 3000 (7777 published) |
| `Flare.Identity` | Users, sessions, PATs, ingest keys, auth settings, audit log (SQLite default, Postgres optional) | library |
| ClickHouse, Redis | Telemetry store; ingest buffer, Data Protection key ring, caches | internal |
| `flare` CLI, `Flare.Mcp` | Clients of the API (PAT bearer); `/mcp` re-enters the API over loopback | n/a |

Intended use: a standing instance on an internal network or behind a reverse proxy, one organisation, many users (RBAC, not multi-tenant SaaS) **[obs]** (authentication-model.md). Optionally internet-exposed for browser/mobile telemetry and public status pages **[obs]** (ADR-0149, status endpoints).

**Out of the product's runtime scope**, so not centred here: `examples/`, `src/website` (marketing site), `src/Flare.Benchmarks`, `*.Tests`, `scripts/`, the MAUI app's own UI (only its telemetry reaches Ingest). `src/Flare.Maui` is a *client SDK/app* that produces untrusted telemetry **[assumed]**.

### Deployment shapes (each changes the model)

1. **Aspire local dev.** Trusted developer machine; auth off; loopback/fixed OTLP ports. Low stakes.
2. **Standalone `docker-compose.yml`.** Publishes ClickHouse **8123** on the host with `default` / `flare` as the default password, plus ingest 4317/4318, api 8080, dashboard 7777 **[obs]** (docker-compose.yml `ports:`, `CLICKHOUSE_PASSWORD:-flare`). Redis has `requirepass flare` and no published port **[obs]**. Auth is off on a fresh DB.
3. **Cluster / Helm.** Postgres identity, multiple replicas, ingress. Helm generates DB passwords when empty and supports `existingSecret` **[obs]** (deploy/helm values.yaml). Per-process state (PAT rate limit, lockout counters if memory-backed) becomes per replica **[obs]** (authentication-model.md).
4. **`flare` CLI standing stack.** Compose-like, local; pins images **[obs]** (memory/roadmap context), same trust as shape 2 **[assumed]**.

---

## Threat Model, Trust Boundaries, and Assumptions

### Trust boundaries

| # | Boundary | Untrusted side | Trusted side | Control |
|---|---|---|---|---|
| B1 | Internet/LAN → OTLP receivers | any client reaching 4317/4318 | Ingest | Ingest key (`IngestApiKeyValidationMiddleware`), **off by default** (`IngestKeyRequired=false`) **[obs]** |
| B2 | Browser/CLI → Flare.Api | any client reaching 8080 | API | Session cookie or `flr_pat_` bearer via `SessionAuthenticationHandler`; **entire authz short-circuits when `AuthSettings.Enabled=false`** (`ConditionalAuthorizationMiddlewareResultHandler`) **[obs]** |
| B3 | Anonymous API surface | internet | API | `/api/auth/*`, `/api/public/status/*`, `/api/alerts/ack-link`, Slack/PagerDuty ack webhooks, Entra/LDAP/OIDC/proxy login, mapped outside `authenticatedRoutes` **[obs]** (Program.cs) |
| B4 | Reverse proxy → API (header auth) | any host able to reach the API directly | proxied identity | Mandatory CIDR allowlist on the TCP peer; no `X-Forwarded-For` trust **[obs]** |
| B5 | API/Ingest/Worker → ClickHouse, Redis, Identity DB | n/a | data plane | Network isolation + shared passwords **[obs]** |
| B6 | Flare → outbound URLs | operator-typed URLs | external/internal services | `AddOutboundGuard` SSRF guard on alert notifiers and OTLP forwarding **[obs]** (ADR-0201) |
| B7 | Flare.Api → Docker socket | n/a | host | `docker-socket-proxy`, `CONTAINERS=1`, `POST=0`, read-only mount, optional profile **[obs]** |
| B8 | Telemetry content → viewers | telemetry authors | dashboard users | Telemetry is attacker-influenced data rendered in the UI; CSP baseline **[obs]** (ADR-0200) |
| B9 | Project scope (ADR-0123) | non-admin member | other projects' services | `ProjectScopeMiddleware` / `ServiceScope.Current` **[obs]** |

### Input control classification

| Input | Controlled by | Notes |
|---|---|---|
| OTLP payloads (log bodies, attributes, `service.name`, trace/span ids, metric names) | **Attacker** (anyone holding or not needing an ingest key; browser keys are public by design) | Parsed in `OtlpLogMapper`, `OtlpTraceMapper`, `OtlpMetricsMapper`, `OtlpJson`; size capped by `Otlp__MaxRequestSizeBytes` (64 MiB default) **[obs]** |
| `Origin` header on ingest | Attacker outside a browser | Spoofable; ADR-0149 says so explicitly **[obs]** |
| Query filters, LogQL, attribute keys/values | Authenticated user (any role incl. Viewer) | Parameterised ClickHouse placeholders (`{p:String}`) in `LogFilterSqlBuilder` **[obs]**; execution caps per query **[obs]** (CLAUDE.md) |
| Dashboard/saved-view JSON, pipeline rule regexes, formulas | Member+ | Regex has a 100 ms timeout in `PipelineRuleExecutor` **[obs]**; synthetic assertions use `NonBacktracking` **[obs]** |
| Alert channel URLs, forwarding targets, source-link RepoUrl, LDAP/OIDC/Entra settings, synthetic monitor targets, LLM endpoint | **Admin or Member (per route group)** | Operator-controlled: not an attacker input unless an account is already compromised |
| `Authorization`, `X-` identity headers via proxy | Proxy (trusted) / anyone (if API directly reachable) | See B4 |
| Signed tokens (ack link, status subscription links, render token) | Holder of a notification/email | Data Protection output, purpose-scoped **[obs]** |
| Compose/Helm/appsettings values, env vars, Data Protection key ring | **Operator/developer** | Never attacker input |
| Source maps, `/v1/screenshots` blobs, `/v1/feedback` | Attacker (ingest-key holder) / CI (admin route for source maps) | Stored in ClickHouse and rendered later **[obs]** |

### Invariants the code must preserve

1. **I1 – Authn gate.** With auth enabled, no endpoint outside the explicit B3 list is reachable without a valid session/PAT; with it disabled, *everything* is open by design. A bug that makes `Enabled` read false when it should be true is a total bypass **[obs]**.
2. **I2 – Role ladder.** Viewer < Member < Admin. Mutations that page people, send requests, or change ingest behaviour are Member+; credentials, auth settings, users, keys, audit, retention, erasure, status pages, forwarding/archive are Admin-only **[obs]** (Program.cs route groups).
3. **I3 – Project scoping.** A non-admin only sees services in their projects across logs, traces, metrics, exceptions, etc. **[obs]** (ADR-0123). *Every new query path must honour `ServiceScope.Current`*; this is the main tenant-like boundary.
4. **I4 – Credential non-disclosure.** Notification secrets are masked in API responses and encrypted at rest; PAT/ingest-key raw values are shown once and stored as SHA-256; passwords are salted PBKDF2 (ASP.NET `PasswordHasher`) **[obs]**. Session tokens are **not** hashed at rest (see Mitigations / gaps).
5. **I5 – No state change on GET** for tokenised links (ack, confirm, unsubscribe) **[obs]** (ADR-0127).
6. **I6 – Outbound requests to user-supplied URLs cannot reach private ranges** unless the operator allow-lists them **[obs]** (ADR-0201), for the covered clients only.
7. **I7 – Audit trail integrity.** Append-only, hash-chained, serialised appends; verify endpoint **[obs]** (ADR-0079, 0202).
8. **I8 – Ingest never trusts telemetry for control flow.** Telemetry content is data; it must not alter queries, routes, filesystem paths or auth decisions.
9. **I9 – PAT = its user, no more.** Same role and project scope; rate limited per token **[obs]** (ADR-0019, 0028).

### Assumptions

- A1 **[assumed]** Flare.Api, ClickHouse, Redis, and the Identity DB are never directly reachable from untrusted networks, except the explicitly published OTLP, API, and dashboard ports. The default compose file contradicts this for ClickHouse (8123 published).
- A2 **[assumed]** TLS is terminated by an operator proxy; `CookieSecure=true` is the default **[obs]** so plain-HTTP access to a non-localhost deployment will not keep a session without changing it.
- A3 **[obs]** A single Flare.Api replica for SQLite; cluster mode needs Postgres.
- A4 **[assumed]** Admins are trusted. Admin → host/RCE-equivalent is not treated as a vulnerability, but Admin → *other-network access* via SSRF-by-design is bounded by the guard where applied.

---

## Attack Surface, Mitigations, and Attacker Stories

### Primary surfaces

1. **OTLP ingest** (gRPC/HTTP logs, traces, metrics, profiles; `/v1/feedback`, `/v1/screenshots`). Parsers for protobuf, OTLP/JSON, Prometheus exposition. Large payloads, decompression, malformed ids, `service.name` spoofing, Drain/pattern clustering memory, cardinality explosion.
2. **Flare.Api authenticated REST + WebSocket live tail** (`/api/logs/tail`). ClickHouse query construction (filters, LogQL, formulas, aggregate), exports (CSV/JSON/XML), MemoryPack and JSON bodies.
3. **Authentication flows.** Local login/bootstrap/set-password/forgot-password, Entra and generic OIDC (OpenIdConnect handlers), LDAP bind (filter escaping via `LdapFilterEncoder`), reverse-proxy header login (`ProxyAuthLoginEndpoints`, `TrustedProxyNetworks`).
4. **Anonymous bearer-ish endpoints.** Ack link (`/api/alerts/ack-link`), Slack interactivity and PagerDuty webhook (signature is the credential; 404 until a secret is set), public status pages and subscriber double-opt-in with rate limiting.
5. **Admin-configured outbound.** Alert channels (webhook/Teams/Discord/incident.io/Jira/PagerDuty/Telegram/Slack/email), OTLP forwarding, S3 archive, source-repo fetch (`SourceFetchRequestBuilder`, sends the stored access token to the configured host), synthetic probes (`Flare.AlertWorker`), LLM endpoint (`Ai:*`, redaction in `AiRedactor`), SMTP.
6. **MCP** (`/mcp`, stateless streamable HTTP; forwards only the Bearer header over loopback so browsers cannot be driven cross-site) **[obs]**.
7. **Dashboard server** (adapter-node): SSR, per-request CSP nonce, `PUBLIC_API_URL`; scheduled-report headless Chromium (optional, `Reports__Enabled`) using a signed Viewer-only render token cookie **[obs]**.
8. **Operator tooling.** `flare` CLI, Aspire AppHost, docker-compose, Helm; Docker-socket-proxy (optional profile).
9. **Supply chain / CI.** Workflows for docker/NuGet/Helm publishing; `tecnativa/docker-socket-proxy` is pinned to `v0.5.0` plus a digest in both compose files **[obs]**.

### Existing mitigations that change severity

- Argument-bound ClickHouse parameters throughout the filter builders; per-query execution/row caps; default 1-hour lookback **[obs]**.
- Login throttling per (username, raw TCP peer IP), generic 401 anti-enumeration, generic LDAP failure path **[obs]**.
- Cookie `HttpOnly`, `Secure=true`, `SameSite=Lax` by default; server-side session revocation; optional idle timeout; absolute 14-day lifetime; sessions listable by one-way handle **[obs]**.
- CORS restricted to `Cors:AllowedOrigins` with credentials; no `AllowAnyOrigin` **[obs]**.
- Baseline security headers on API and dashboard (ADR-0200); HSTS only for HTTPS requests.
- SSRF connect-time guard (resolves, rejects if *any* address is non-public, connects to the checked IP, covers redirects/rebinding) on notifiers and forwarding **[obs]**.
- Notification secrets, SSO/LDAP secrets, source-link tokens and telemetry-export credentials encrypted with Data Protection (`enc:v1:`); masked on read (ADR-0205, 0212, 0213).
- Per-key ingest limits, origin and service allowlists, key expiry; limit failures fail **open** if Redis is down (documented as a fairness guard, not a security boundary) **[obs]**.
- PAT/ingest key stored as SHA-256 of a high-entropy random token; PAT optional expiry; per-token rate limit.
- Audit log with hash chain, export, forwarding, read auditing.
- Proxy-auth: mandatory CIDR allow-list, `0.0.0.0/0` rejected.
- Pipeline-rule regex timeout; synthetic regex `NonBacktracking`.
- No state change from GET on tokenised links.
- Docker-socket-proxy scoped to container list/inspect.

### Realistic attacker stories

| # | Attacker | Story | Why it is realistic |
|---|---|---|---|
| S1 | Network-adjacent, no creds | Instance left on defaults: auth off, ingest key not required, ClickHouse 8123 published with `default/flare`. Reads all telemetry (often secrets/PII), writes arbitrary rows, or uses ClickHouse as a pivot. | Defaults are intentionally open; compose publishes the ports **[obs]** |
| S2 | Unauthenticated external sender | Floods or poisons telemetry: fake logs/traces under a victim `service.name`, cardinality bombs, huge payloads, to hide activity or trigger alert-notification spam/cost. | Ingest key off by default; browser keys are public; `Origin` spoofable **[obs]** |
| S3 | Telemetry author → dashboard user | Stored XSS or content spoofing through log bodies, attributes, exception messages, span names, source-map names, markdown/ANSI rendering, panel descriptions; goal: steal a session (HttpOnly mitigates) or perform actions as the viewer (same-site API calls with the cookie). | Telemetry is inherently attacker-influenced; Svelte escapes by default, so risk sits in `{@html}`, ANSI/markdown/highlighting paths **[assumed, verify]** |
| S4 | Authenticated Viewer | Escalate role or read other projects' data: missing `ServiceScope` on a new query path, IDOR on dashboards/saved views/alerts (global, shared by design), `/api/users` mutations reachable at lower role, export endpoints bypassing scope. | Project scoping is a cross-cutting concern re-implemented per query service |
| S5 | Authenticated Member | Abuse Member-reachable outbound features: synthetic monitors (explicitly *not* SSRF-guarded because they exist to reach internal hosts), alert channel URLs (guarded), dashboard schedules (emails rendered dashboards), AI NL-filter prompts (prompt injection against the configured LLM). | Member route group includes these **[obs]**; synthetic probes unguarded by design **[obs]** |
| S6 | Holder of a notification/email | Replays an ack/confirm/unsubscribe link. Bound to incident and lifetime; works for any holder. | Bearer by design **[obs]** (ADR-0127) |
| S7 | Network attacker reaching API directly in proxy-auth mode | Sets the identity header from an in-range IP or via a compromised neighbour on the trusted CIDR; becomes Admin. | Documented sharp edge; operator must keep API unreachable except via the proxy **[obs]** |
| S8 | Attacker with DB/volume/backup read | Reads `Sessions.Id`, which is the raw session token (not hashed), and replays it as a cookie; also exports telemetry. Admin API keys and PATs are hashed so a leak of the table does not leak them. | Observed in `Migrations/Sqlite/0001_identity.sql` comment and `DbSessionStore` (no hashing) **[obs]** |
| S9 | Attacker who steals a PAT/ingest key from CI/browser | PAT: acts as that user via REST and, for a non-browser client sending `Authorization: Bearer` on the upgrade, the WebSocket (the PAT rate limit counts only the upgrade). Browser ingest key: restricted by origin/service allowlist only if configured. | Common leak paths: CI logs, JS bundles |
| S10 | Compromised dependency / image | `docker-socket-proxy` pinned by tag and digest (bump deliberately); NuGet/Docker/Helm publish workflows; the proxy holds the Docker socket (read-only mount, but the proxy process has full socket access if the image is malicious). | Optional `resource-graph` profile only **[obs]** |
| S11 | Authenticated user + ClickHouse resource abuse | Expensive but valid queries (wide aggregates, regex over body, many parallel live-tail sockets) to exhaust ClickHouse/Redis. | Caps exist; live-tail connection count is not obviously capped **[assumed]** |
| S12 | CSRF / cross-site WebSocket | Drive state-changing API calls or open `/api/logs/tail` using the victim's cookie from another site. | `SameSite=Lax` blocks cross-site POST/WS cookies in modern browsers; `LogTailEndpoints` has no `Origin` check and the API has no antiforgery token **[obs]**. Same-*site* attackers (another service on the same registrable domain) are not stopped. If `Auth:CookieSameSite=None` is configured, this becomes real **[obs]** option exists |

### Explicitly out of scope / implausible

- **Admin-to-host compromise** (e.g., admin sets a webhook URL, configures LDAP/OIDC, uploads source maps). Admin is trusted (A4). Admin-only SSRF via *unguarded* fixed clients (source fetch, S3 archive, LLM endpoint, SMTP) is Low at most.
- **Operator-controlled configuration** (`appsettings`, env vars, compose files, Helm values, `Outbound__*`, `Identity__*`): not attacker input.
- **Developer-only paths**: `examples/`, `scripts/`, `src/Flare.Benchmarks`, tests, demo seeders, `deploy-demo.yml` target.
- **Multi-tenant SaaS isolation.** Flare does not claim it. Saved views, alert rules, channels and templates are shared workspace objects by design; only dashboards have owners (ADR-0027) and services have project scope (ADR-0123).
- **Vulnerabilities requiring physical access** to the host, or arbitrary read of the Redis Data Protection key ring *and* ClickHouse (ADR-0205 states this is not protected).
- **DoS by an Admin or an ingest-key holder within their configured limits.**
- **Log injection that only affects the integrity of telemetry** for an instance whose operator chose anonymous ingest.

---

## Actors & Roles

| Actor | Holds | Can do | Is "requires admin/auth" a real barrier? |
|---|---|---|---|
| Anonymous (auth enabled) | Network reach | Login/bootstrap flows, public status pages, subscribe/confirm/unsubscribe (rate-limited), ack link with valid token, Slack/PD webhooks with valid signature | n/a |
| **Anonymous (auth disabled, the default on a fresh DB)** | Network reach | **Everything an Admin can do**, since authz short-circuits | **No barrier at all.** Any finding that "requires admin" is exploitable by anyone on such an instance; triage against the configured state |
| Bootstrap race (fresh DB, auth enabled, zero users) | Network reach | First `POST /api/auth/bootstrap` creates an Admin; a benign race can create two (documented) | Barrier only after the first account exists |
| Ingest-key holder | `Authorization: Bearer <key>` | Write telemetry (subject to limits, origin/service allowlists, expiry). No read. Includes `/v1/feedback`, `/v1/screenshots` | Real barrier when `IngestKeyRequired=true`; **not** when it is false |
| Static ingest key holder | `Ingest:StaticIngestApiKey` | Same, unlimited, never expires, cannot be restricted or revoked via API | Operator secret |
| Viewer | Session or PAT | Read all in-scope telemetry, own PATs/sessions/preferences, own dashboards | Real barrier for Member/Admin routes |
| Member | Session or PAT | Viewer + create/edit alerts, channels (with secrets), templates, SLOs, rotations, maintenance windows, synthetic monitors, dashboard schedules, pipeline rules, metric rules, log metrics, error ownership, releases | Real barrier, but Member can already cause outbound requests and page people |
| Admin | Session or PAT | Everything: users, keys, auth config, audit, retention (deletes data), erasure, status pages, forwarding/archive, source maps/links, projects | Highest app role; unrestricted by project scope |
| Project member | Project role | Scoped to a project's services (ADR-0123) | Boundary for I3 |
| Service account | User row with kind `ServiceAccount` + PAT | Whatever role it was given | Same as the role |
| PAT holder | `flr_pat_...` | Exactly the owning user's role and scope; per-token rate limit; not usable on the WebSocket | A leaked PAT of an Admin is an Admin session without cookies |
| Reverse-proxy-asserted identity | Trusted header from an in-range peer | Session as the named user, role from groups header or default | Only as strong as network isolation of the API |
| Scheduled-report renderer | Signed render token | Viewer-level read of one dashboard | Scoped to `RenderDashboardId` claim **[obs]** |
| Notification-link holder | Ack/confirm/unsubscribe token | One action on one incident/subscription | Bearer by design |
| Flare.AlertWorker / Ingest / Api (service-to-service) | ClickHouse and Redis passwords, shared Identity DB, shared Data Protection ring | Direct data-plane access | Shared secrets; compromise of any one reaches all |
| Operator / developer | Deployment config, images | Total | Out of the attacker model |

---

## Data Classification

| Data | Sensitivity | Where it lives | Leaves the system to |
|---|---|---|---|
| Logs, span attributes, exception stacks, metric labels | **High, uncontrolled.** Commonly contain PII, tokens, connection strings, request bodies | ClickHouse (`logs`, `spans`, `exceptions`, …), Redis Streams (transient), live-tail sockets | Dashboards, exports (CSV/JSON/XML), MCP/CLI, forwarding targets, S3 archive, LLM (after `AiRedactor`), notification bodies (log excerpts) |
| Managed PII redaction / data-subject erasure | Control, not data | ADR-0198/0199 pipeline and `Erasure` | Erasure mutations are async (`ALTER … DELETE`) |
| Screenshots, user feedback, session replay-lite | **High** (can show on-screen PII) | ClickHouse blobs | Dashboard |
| Password hashes | High | Identity DB (PBKDF2 via ASP.NET `PasswordHasher`) | none |
| **Session tokens** | **High** | Identity DB `Sessions.Id` in **plaintext**; cookie `flare_session` | Cookie only |
| PAT, ingest keys | High | SHA-256 hash only; raw shown once | Client-held |
| Static ingest key, SMTP password, ClickHouse/Redis/Postgres passwords, archive keys | High | Env/config/Helm secrets | Process env, logs if mis-logged **[assumed]** |
| Notification channel secrets (webhooks, bot tokens, routing keys, Jira/incident.io/JSM tokens) | High | ClickHouse `notification_channels`, `alert_rules` as `enc:v1:` ciphertext; **key ring in Redis, unencrypted** | Outbound requests; masked in API |
| SSO/LDAP settings (client secret, bind password) | High | Identity DB **[assumed encrypted? check]** | Outbound to IdP/LDAP |
| Audit log | Integrity-critical, moderate confidentiality (actor, source IP, field diffs, redacted) | Identity DB (append-only trigger + hash chain) | Export, forwarding sinks, read-audit entries |
| Alert definitions, dashboards, saved views | Low–moderate | ClickHouse/Identity | Exports, Grafana import/export |
| Status-page subscriber emails | PII | Identity/ClickHouse | Mail |
| Data Protection key ring | **Critical key material** | Redis | Shared Api/Worker |

---

## Severity Calibration

Severity depends on the **deployment state**: first ask whether auth is enabled and ingest keys required. Findings that only apply with defaults left open are *configuration exposure*, usually Medium or documented, not Critical, unless Flare silently makes the state worse.

### Critical
Unauthenticated (or Viewer-reachable, with auth on) compromise of the whole instance or its data plane.
- Auth bypass when `Enabled=true` (e.g., a route mapped outside `authenticatedRoutes` that mutates; handler fallthrough to success; session or PAT validation skipping disabled/expired state).
- Reverse-proxy header trust bypass (header honoured from a peer outside the CIDR, `X-Forwarded-For` trusted, `0.0.0.0/0` accepted).
- SQL injection into ClickHouse through a filter, LogQL, formula, or export builder that interpolates user text instead of using `{param:Type}`.
- RCE via parser (OTLP/JSON, MemoryPack, source map, Prometheus text, Drain), unsafe deserialisation, or the headless Chromium report renderer.
- Mass credential disclosure: unmasked notification secrets or session tokens in an API response or the audit log.

### High
Role/scope violations and cross-boundary data exposure for an authenticated or ingest-key attacker.
- Viewer → Member/Admin (missing `RequireMember/RequireAdmin`, wrong group, PAT with broader reach than its user).
- Project-scope bypass: any read path that ignores `ServiceScope.Current` (search, aggregate, exports, MCP, live tail, exceptions, resource graph, dashboards panels).
- Stored XSS from telemetry content that runs in an authenticated dashboard session and can call the API.
- SSRF that bypasses `AddOutboundGuard` on a covered client, or a new user-URL client added without the guard, reaching metadata or ClickHouse.
- Ingest key bypass (expired, revoked, origin or service allowlist circumvented on a code path such as `/v1/feedback` or a new receiver).
- Forgeable or replayable signed tokens (ack/confirm/render) beyond their stated scope; wrong Data Protection purpose.
- Session fixation, missing revocation on disable or password reset.

### Medium
Bounded impact, or exploitation depends on a non-default or already-privileged state.
- Missing `Origin` check on `/api/logs/tail` or CSRF defences that only hold because of `SameSite=Lax`.
- Plaintext session tokens in the Identity DB (needs DB read access).
- Unguarded outbound: synthetic probes (intended), source fetch, archive, LLM endpoint reachable by a Member/Admin only.
- Login throttle gaps (behind a proxy all clients share one IP key; per-replica counters in cluster mode).
- Resource exhaustion by an authenticated user within caps (many live-tail sockets, regex over `Body`), or by an unauthenticated sender on an instance with ingest key off.
- Information disclosure in error messages, send-test responses (body excerpt), or `X-Flare-Warning`.
- Default `flare` ClickHouse password with published port in the standalone compose (configuration exposure, but Flare ships it).
- Audit log gaps (an Admin action not audited; chain verification false-negative).
- Prompt injection into the NL-filter/AI features limited to wrong output (no tool access).

### Low
- Admin-only SSRF/path/command effects (Admin is trusted).
- Operator-config footguns documented in how-tos (broad trusted CIDR, auth left off).
- Telemetry content spoofing on an instance that allows anonymous ingest.
- Verbose logging, missing hardening headers on static assets served by sirv before the SvelteKit handler.

### Not a vulnerability here
Anything needing attacker control of `appsettings`, env, compose, Helm values, or the Data Protection key ring; findings in `examples/`, `scripts/`, `src/website`, benchmarks, and tests; "requires admin" for Admin-by-design capabilities **on an instance with auth enabled**.

---

## Open questions for maintainers

1. Is ClickHouse **8123** intentionally published in `docker-compose.yml` with the default `flare` password? Should the default bind to 127.0.0.1?
2. Should a fresh install nudge or force enabling auth and ingest keys when ports are bound beyond loopback, or is "open by default" the long-term stance?
3. Are the dashboard (`{@html}`, ANSI, markdown, code highlighting) rendering paths audited for telemetry-derived XSS? None were verified in this pass **[assumed]**.
5. Should `Sessions.Id` be stored as a hash (like PATs and ingest keys)? Current design keeps the raw token because the cookie value is the primary key.
6. Does the live-tail WebSocket need an `Origin` check and a per-user connection cap?
7. Is every query-service path covered by `ServiceScope` tests (I3), including exports, MCP tools, Prometheus-compat endpoints, and resource-graph?
8. Is the cluster/Helm deployment expected to run behind a WAF or ingress that rate-limits anonymous `/api/auth/*`, `/api/public/status/*`, and OTLP?
9. Should the unguarded outbound clients (source fetch, archive, LLM endpoint) share the SSRF guard, with an opt-out, for consistency?
10. Where does the trusted-proxy model run in Kubernetes (pod CIDR / ingress controller range)? Is a default range documented?
11. Is the Data Protection key ring in Redis protected by an `IXmlEncryptor` in Helm/compose deployments, and is Redis persistence/backup in scope?
12. Is `Flare.Maui` considered a trusted first-party client or part of the untrusted-telemetry surface?

*Evidence gaps in this pass:* the LDAP/OIDC/Entra code paths, the dashboard components, individual Member-route handlers, the Helm templates, and Flare.AlertWorker were read at the wiring/ADR level only. Treat every **[assumed]** claim as a hypothesis for the next review.
