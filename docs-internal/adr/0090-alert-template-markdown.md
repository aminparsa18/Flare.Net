# ADR-0090: Markdown in alert notification templates

Status: Accepted

Date: 2026-10-03

## Context

Custom templates (ADR-0052) were sent as plain text. Telegram dropped
`parse_mode` for them because user text isn't guaranteed to be valid Telegram
Markdown, and email had no HTML part, so a template could not carry a bold
heading or a labelled link. Prior art:
[signoz#10682](https://github.com/SigNoz/signoz/commit/30d3f754b56b39c4660ca18bdf8e4d8d0a38b845).

## Decision

`AlertMarkdown` renders a template as a small CommonMark subset, in the format
of the channel receiving it. No schema change; extends ADR-0052.

- **Subset:** `**bold**`, `*italic*`/`_italic_`, `` `code` ``, `[label](https://…)`,
  `-`/`*` bullet and `1.` numbered lists, `\` escapes. Nothing else is
  interpreted (no headings, tables or raw HTML), so a template can't emit
  markup a channel wouldn't accept. `_` emphasis doesn't start inside a word,
  so `snake_case` in an existing template is unchanged.
- **Placeholders are atoms.** `{{...}}` is resolved before parsing and the
  value is treated as opaque text: never scanned for markers, always escaped
  for the target format. A service named `my_*_svc` or a log message with `**`
  can't change formatting or inject HTML.
- **Links:** only `http`, `https` and `mailto` become links. A link whose URL
  resolves empty (`{{rule_url}}` with no `Alerting:PublicUrl`) collapses to
  its label.
- **Per channel:**

  | Channel | Output |
  | --- | --- |
  | Telegram | `parse_mode: HTML` (`b`, `i`, `code`, `a`), all text HTML-escaped |
  | Webhook to `hooks.slack.com` / `hooks.slack-gov.com` | Slack mrkdwn in `text`; `& < >` escaped |
  | Webhook, other | plain text: markers stripped, links as `label (url)` |
  | Email | `multipart/alternative`: plain text plus an HTML part. A title-only template stays plain (the title is the subject) |
  | PagerDuty | plain text |

  Slack is detected from the webhook host rather than a new channel type or
  setting: Slack incoming webhooks have fixed hostnames, and a Slack-compatible
  receiver elsewhere simply gets plain text.
- **Fallback:** if the Markdown pass throws, the notification is sent with the
  plain placeholder substitution (ADR-0052 behaviour). The built-in wording is
  unchanged (Telegram keeps `parse_mode: Markdown` for it).
- **Preview:** `POST /api/alerts/notification-preview` additionally returns
  `telegramHtml`, `slackText` and `emailHtml`, shown under "Per-channel output"
  in the rule form. `title`/`text` are now the plain rendering.

## Consequences

- Existing templates with literal `*x*`, `**`, backticks, `[a](https://b)` or
  leading `- ` lines now render formatted, and generic-webhook `text` loses
  those markers. Plain prose is unaffected. Escape with `\` to keep a marker.
- Slack mrkdwn has no escape for `*`/`_`/`` ` `` inside values, so a value
  containing them may be styled by Slack. `&`, `<` and `>` are escaped, so
  there is no mention or link injection.
- Not supported: headings, tables, nested lists, images, Slack Block Kit
  (still a named follow-up in `WebhookAlertNotifier`).
