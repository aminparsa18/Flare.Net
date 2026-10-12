# Data residency and privacy

Where does the data in a Flare deployment live, and what can leave the machine?
This page answers the questions a security or compliance review asks first:
where each store sits, which outbound connections exist, which of them are on by
default, and what that means for a data processing agreement (DPA). For the
steps to turn individual features off, follow the links in each section.

## Flare is self-hosted, so you hold the data

Flare is software you run. The project operates no hosted service, receives no
telemetry from your instance, and never has access to your data. There is no
Flare account to create and no licence server to reach.

- **No phone-home.** Nothing in the platform reports usage, crashes, hostnames
  or content to the project. The dashboard bundles its fonts and scripts, so
  opening it loads nothing from a third-party CDN, and none of the services
  embed an analytics or error-reporting SDK.
- **One call on its own, and you can turn it off.** `Flare.Api` checks GitHub
  for a newer release so the dashboard can show a "new version available"
  notice
  ([ADR-0068](../../docs-internal/adr/0068-update-available-notice.md)). It is
  described under [the update check](#the-update-check) below.
- **Everything else is something you configure.** Each other outbound
  connection needs a destination you enter: a notification channel, an AI
  endpoint, a forwarding target, an archive bucket. With none configured, Flare
  talks only to its own components and to the telemetry sources and identity
  providers you point it at.

## Where each store lives

| Data | Store | Where it lives |
|---|---|---|
| Logs, traces, metrics, profiles, exceptions and their rollups | ClickHouse | The `clickhouse-data` volume (or your cluster's disks) |
| Cold-tier telemetry, if you enable [tiering](retention-and-tiering.md) | S3-compatible bucket | The bucket you configure (RustFS in the bundled overlays) |
| Events accepted but not yet inserted | Redis Streams | The `redis-data` volume |
| Users, sessions, API keys, personal access tokens, alert rules, notification channels, dashboards, saved views, audit log | Identity store | An embedded SQLite file in the `identity-data` volume, or PostgreSQL if `Identity__Provider=Postgres` |
| Source-repository tokens, webhook URLs, bot tokens, SMTP and AI keys | Identity store or configuration | Stored in the identity store; encrypted with the ASP.NET Core Data Protection key ring |

Every one of these is on infrastructure you choose. The region, the cloud
account and the disk encryption are yours to pick, and moving data between
regions is a backup and restore on your side. Flare adds no replication to
anywhere else.

Two points that matter for residency reviews:

- **Telemetry can contain personal data.** Log messages, span attributes and
  exception messages are whatever your applications emit. Flare stores them
  verbatim unless you configure the managed PII redaction pipeline rule, which
  masks matches at ingest before they reach ClickHouse.
- **Retention is yours to set.** By default Flare keeps everything. Set a TTL per
  signal to meet a retention limit; see
  [Retention and tiering](retention-and-tiering.md).

## What can leave the machine

Each row below is off until you configure it, except the update check. In every
case the destination is one you chose, and the payload is described in the
linked page.

| Feature | Destination | What is sent | Default |
|---|---|---|---|
| [Update check](#the-update-check) | `api.github.com` | A release-list request. No instance data. | **On** |
| [AI explanations and alert summaries](#ai-providers) | Your OpenAI-compatible endpoint | A redacted prompt: exception text, stack trace, optionally source lines | Off |
| [Alert notifications](#notification-channels) | Webhook, Slack, Teams, Discord, Telegram, PagerDuty, incident.io, Jira, JSM Ops, SMTP server | The alert text and a link back | Off until a channel exists |
| [Telemetry forwarding](../how-to/data/forward-telemetry-to-another-otlp-endpoint.md) | Any OTLP endpoint | The OTLP requests Ingest accepted | Off |
| [Telemetry archive](../how-to/data/archive-telemetry-to-s3.md) | Your S3-compatible bucket | Parquet or NDJSON exports of hourly windows | Off |
| [Source snippets](../how-to/investigate/link-exceptions-to-source-code.md) | Your Git host (GitHub, GitLab, Azure DevOps) | A request for a file at a commit, using the token you stored | Off until a repository is linked |
| [Synthetic monitors](../how-to/monitor/synthetic-monitoring.md) | The URLs and hosts you list | An HTTP, TCP, ping or certificate probe | Off until a monitor exists |
| [Status page](../how-to/monitor/publish-a-status-page.md) subscriber email, scheduled dashboard reports | Your SMTP server | Incident updates and report content to the addresses you entered | Off until configured |
| Prometheus scrape | The targets you list | Nothing: Flare pulls `/metrics` from them | Off |
| Single sign-on | Entra ID, your OIDC provider or your LDAP server | The sign-in exchange for the user logging in | Off until you enable a provider |

### The update check

`Flare.Api` asks the GitHub releases API for the latest version, at most once per
`UpdateCheck__Interval` (24 hours by default), and only when a dashboard requests
`/api/version`, so an idle instance makes no calls. The request carries a
`User-Agent` of `Flare/<your version>` and nothing else about the instance; the
response is a public release list. The IP address of the API host is visible to
GitHub, as with any HTTPS request.

Set `UpdateCheck__Enabled=false` to stop it. Flare then never contacts GitHub and
the dashboard shows no notice. Air-gapped installs should set this.

### AI providers

The AI features (explain an exception, summarize an alert, natural-language
filters) are off unless `Ai__Enabled=true` and an endpoint and model are set.
Flare has no built-in provider; you bring any OpenAI-compatible endpoint. A
self-hosted model such as Ollama or vLLM on your own network keeps the prompt
inside your boundary. A hosted provider receives the prompt, so it is a
sub-processor of yours for those requests.

Before a prompt is sent, Flare redacts JWTs, bearer and basic tokens, URL
credentials and similar secrets, and caps the length
([ADR-0103](../../docs-internal/adr/0103-explain-exception-llm.md)). Redaction is
pattern-based and best-effort: redacted-but-still-sensitive text, such as
personal data inside an exception message, can still reach a hosted model.
Read-only [assistant connections over MCP](../how-to/integrations/connect-ai-assistants.md)
are different: the assistant you connect queries Flare with a token you issued,
and what it then does with the answers is governed by that assistant's terms.

### Notification channels

An alert notification goes to the channel you configured, over the transport
that channel needs. Slack, Teams and Discord are incoming-webhook URLs;
PagerDuty, incident.io, Jira and JSM Ops call their vendors' APIs; email uses
your SMTP server. The message holds what the alert template renders: by default
the rule name, state, value and a link, plus any matched log text you add to a
template. If a template could carry personal data, the channel's vendor holds it
once delivered, so review
[alert templates](../how-to/alerting/share-alert-notification-templates.md) with
that in mind. Webhook URLs and bot tokens are stored encrypted and shown masked.

## Air-gapped and restricted-egress installs

To run with no outbound traffic at all:

1. Set `UpdateCheck__Enabled=false` on `Flare.Api`.
2. Leave `Ai__*`, `Forwarding__*`, `Archive__*` and the notification channels
   unconfigured, or point them at endpoints inside your network.
3. Use local accounts, LDAP, or an OIDC provider you host, instead of Entra ID.
4. Pull the container images through your own registry; Flare does not fetch
   anything at runtime.

## What this means for a DPA

Because you run the software, the usual roles are:

- **You are the controller**, and the processor if you run it for others. The
  Flare project is neither: it does not receive, store or process your data, so
  there is nothing for it to sign as a processor and no Flare-operated
  sub-processor to list.
- **Your sub-processors are the services you enable** from the table above: your
  cloud or hosting provider for the stores, a hosted AI provider if you use one,
  the vendors behind your notification channels, your SMTP provider, and your
  identity provider. List the ones you turn on in your own sub-processor
  register.
- **The code is open**, so a reviewer can confirm the claims on this page. The
  outbound calls live in `Flare.Api/Updates`, `Flare.Api/Ai`,
  `Flare.Api/Alerting`, `Flare.Ingest/Forwarding` and `Flare.AlertWorker/Archive`.

This page describes how the software behaves; it is not legal advice, and it does
not replace your own assessment of the data your applications send to Flare.

## See also

- [Architecture](architecture.md)
- [Retention and tiering](retention-and-tiering.md)
- [Authentication model](authentication-model.md)
- [Projects and access scoping](projects-and-access-scoping.md)
