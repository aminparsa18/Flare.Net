# How to extract or redact fields at ingest

Set up **pipeline rules** — regex-based extraction and redaction, and JSON
body parsing, applied to logs before they're written to storage. Use extraction to pull structured
fields (like a user ID or status code) out of a log's message into a proper,
queryable attribute. Use redaction to mask sensitive data (like a credit
card number or an email address) so it's never stored in the clear. For the
design behind this, see
[ADR-0033](../../../docs-internal/adr/0033-pipeline-rules-extraction-redaction.md)
and [ADR-0034](../../../docs-internal/adr/0034-pipeline-rules-preview.md)
(preview mode).

Pipeline rules run once, at ingest — before Drain's pattern clustering, and
before anything lands in ClickHouse. Preview a rule before saving it (see
[Preview a rule before saving](#preview-a-rule-before-saving) below) to
confirm it behaves the way you expect, rather than saving it blind.

## Prerequisites

- A running Flare instance you can reach in a browser (any of
  [standalone](../deploy/run-standalone.md), [Aspire](../deploy/run-with-aspire.md), or the
  [CLI](../deploy/run-with-cli.md)) with some data already flowing in.

## Create a redaction rule

![Pipeline Rules list with redaction, extraction and Parse JSON rules, one disabled](../../screenshots/manage-pipeline-rules-en.webp)

1. Open the **⋯** menu at the top right and pick **Pipeline Rules**.
2. Click **New rule**. Give it a name (e.g. "Redact card numbers").
3. Under **Applies to**, optionally narrow the rule to specific services or
   log levels, or leave it unscoped (see [Scope a rule](#scope-a-rule-dont-leave-it-unscoped)).
4. Under **Actions**, pick **Redact** and enter a regex pattern to match
   (e.g. `\d{16}` for a 16-digit card number). Leave the source field blank
   to redact `Body`, or enter an attribute key to redact that attribute's
   value instead. The replacement text defaults to `***`.
5. Click **Create rule**.

From here on, any log matching this rule has the matched text replaced
before it's written — including the pattern template Drain's log-clustering
feature computes, so a redacted body never leaks into a cluster template
either.

![Editing a redaction rule that masks card numbers](../../screenshots/manage-pipeline-rules-2-en.webp)

Rule names are unique (case-insensitive): saving a rule whose name another rule already uses is rejected with a 409, so tooling can address a rule by name.

## Create an extraction rule

1. Open **Pipeline Rules** → **New rule**.
2. Under **Actions**, pick **Extract fields** and enter a regex pattern with
   **named capture groups** — the group name becomes the new attribute key
   directly. For example, `user_id=(?<user_id>\d+)` against a body like
   `"...for user_id=42..."` adds a `user_id: "42"` log attribute.
3. Click **Create rule**.

Extracted attributes show up like any other log attribute — filterable in
the Logs Explorer, usable as an alert rule condition, the same as an
attribute the source application set directly.

## Turn a JSON log body into attributes

If an app logs a JSON string as its body (Serilog's JSON formatter, the
.NET console JSON formatter, most Node and Python structured loggers),
one rule turns every field into a log attribute. You don't need a regex.

1. Open **Pipeline Rules** → **New rule**, and scope it to the services
   that log JSON.
2. Under **Actions**, pick **Parse JSON**. Leave the source field blank
   to parse `Body`, or enter an attribute key whose value is JSON.
3. Optionally set:
   - **Key prefix** — prepended to every key (e.g. `json.` gives
     `json.user.id`). Without one, a parsed key overwrites an attribute
     of the same name.
   - **Max depth** — how many object levels to flatten (default 5, at
     most 10).
   - **Max keys** — the most attributes to write per log (default 100,
     at most 500).
4. Click **Create rule**.

A body like `{"msg":"login","user":{"id":7,"name":"ada"},"tags":["a","b"]}`
adds `msg=login`, `user.id=7`, `user.name=ada` and `tags=["a","b"]`.
Nested keys are joined with `.`. Arrays, and objects deeper than
**Max depth**, are kept whole as JSON text. `null` values are skipped.
A body that isn't a JSON object is left alone, and the body itself is
never changed. For the design behind this, see
[ADR-0070](../../../docs-internal/adr/0070-pipeline-rules-parse-json.md).

## Preview a rule before saving

![Preview: 20 of 20 sampled logs would change, with each log's before and after text](../../screenshots/manage-pipeline-rules-3-en.webp)

Click **Preview** in the create/edit dialog at any point while you're
filling it in — it's not gated on saving first. Flare pulls up to 20 of
the most recent logs already matching the rule's **Applies to** condition
and runs the actions you've configured against each one, in-process,
without saving anything or touching real ingest traffic. You'll see a
summary ("N of 20 sampled logs would change") and each sampled log's
before/after text, with changed ones marked.

Preview always runs against the dialog's current, unsaved field values —
even while editing an already-saved rule — so it reflects whatever pattern
you're actively typing, not the last-saved version. If no logs currently
match the condition, preview says so rather than showing an empty list;
try widening the condition or picking a service you know is actively
logging.

## Scope a rule (don't leave it unscoped)

A rule with no service, level, or search filter set matches **every** log —
the create/edit form shows an explicit warning banner when this is the
case, so it's a decision you make on purpose rather than something that
happens by accident. Prefer narrowing to the specific service(s) a pattern
is meant for, especially while you're still confirming a new pattern
behaves the way you expect.

## Ordering, when more than one rule applies

Rules run in the order they were created, each seeing the previous rule's
output. If you need a field extracted from the *original* body before
another rule redacts it, create the extraction rule first.

## Turn a rule off without deleting it

Toggle **Enabled** off in the create/edit dialog (or the rule's row in the
table shows a **Disabled** badge once you do). A disabled rule stays saved
but stops being applied — useful for pausing a rule while you investigate
without losing its configuration.

## Turn on managed PII redaction

Instead of writing regex rules by hand, `Flare.Ingest` ships a built-in
detector set that redacts email addresses, payment card numbers
(Luhn-checked), IBANs (mod-97-checked), JWTs, `Bearer` tokens and PEM
private keys. It is off by default. Turn it on with configuration on
`Flare.Ingest` (restart required), or from the dashboard (below):

```
PipelineRules__Pii__Enabled=true
# optional: narrow the detector set (default: all)
PipelineRules__Pii__Detectors=Email,CreditCard,Jwt
```

Valid detectors are `Email`, `CreditCard`, `Iban`, `Jwt`, `BearerToken` and
`PrivateKey`. Each match is replaced with a labelled mask such as
`[REDACTED:email]`. It scans every log's body and log-attribute values (not
resource or scope attributes or metrics), runs
before your own rules, and before pattern clustering. Digit runs that fail
the checksum, such as order ids and timestamps, are left alone. It is
best-effort pattern matching, not a guarantee; keep custom rules for
anything specific to your data. Setting `PipelineRules__Enabled=false`
turns off managed redaction too.

Spans are covered too: span attributes, the status message, and span event and link attributes (where `exception.message` and `exception.stacktrace` live) are redacted the same way. The span name is not scanned.

Before enabling it, click **Preview PII redaction** on the Pipeline Rules page. It scans up to 500 recent logs, or spans via the **Spans** tab, and shows what would be masked; nothing is written.

The same dialog has **Scan fields for PII**, a report-only scan that lists which log body or attribute fields (or, on the **Spans** tab, span status message and attribute fields) look like PII, per detector, with a count and one masked example. Use it to find where PII lives before deciding what to enable.

### Switch it from the dashboard

Admins can override the configured value without a restart. On **Settings >
Pipeline rules**, the *Managed PII redaction* card offers **Follow
configuration** (no override, the default), **On** and **Off**. `Flare.Ingest`
polls the saved choice every 30 seconds (`PipelineRules__RefreshInterval`), so
a change applies within about that long on every replica. The choice wins over
`PipelineRules__Pii__Enabled`; the detector list stays configuration-only.
Redaction is not retroactive: events stored before the switch keep their
original values. If the setting cannot be read, `Flare.Ingest` keeps the last
value it saw. Members can see the card; only Admins can change it, and every
change is written to the audit log (`pii-redaction-setting`).

## Changes take a little time to apply

A newly created, edited, or disabled rule takes up to 30 seconds to take
effect — `Flare.Ingest` polls for rule changes on that interval rather than
being notified instantly. This is a fixed interval, not currently
configurable from the dashboard.
