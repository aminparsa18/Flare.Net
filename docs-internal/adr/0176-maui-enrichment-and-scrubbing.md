# ADR-0176: MAUI enrichment and attribute scrubbing

Status: Accepted

Date: 2026-10-10

## Context

`Flare.Maui` ([ADR-0166](0166-maui-sdk.md)) sends device and session attributes and nothing about the user, so release
health ([ADR-0175](0175-release-health.md)) has no users to count, and an app has no way to add its own global
attributes or remove something sensitive before it leaves the device.

## Decision

- **`FlareMaui.SetUser(id, name?, email?)`, `SetTag(key, value)` and `SetContext(name, values)`** keep a small
  in-process attribute set that is stamped on every span (when it starts) and log record (when it ends). A null or
  empty value removes an entry; `SetContext` replaces a named group, written as `name.key`. Resource attributes are not
  used, because they are fixed when the providers are built and the user changes at runtime.
- **`SendDefaultPii` (default off)** gates the identifying fields: with it off `SetUser` sends only `user.id`, which
  the app chose and which is typically a pseudonymous account id. Name and e-mail need the opt-in. No device
  identifier is sent in either mode.
- **`ScrubAttribute(key, value) -> value?`** runs over every span tag when the span ends and over every log attribute,
  after the enrichment processors, so it also sees `user.*` and tags. Return a new value to redact or null to remove.
  A throwing scrubber is ignored for that attribute, so a bug in it cannot break the app's telemetry.
- **Attribute-level only, not a span-dropping `BeforeSend`.** Dropping a whole span from a processor means clearing its
  recorded flag, which is not verified here without a device build; scrubbing covers the common PII need.

## Not decided here

Scrubbing resource attributes, span names and exception messages (a stack message can hold PII), and the disk-retry
size and age limits, which the exporter's experimental retry does not expose in the version used.
