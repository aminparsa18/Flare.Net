# How to share notification wording across alert rules

A notification template is a named title and body that many alert rules can use.
Edit it once and every rule that uses it sends the new wording.

## Create a template

Open **Settings > Notification templates** and choose **New template**. Fill in:

- **Title** and **Body (fired)**, using the same `{{placeholder}}` syntax as a
  rule's own message (for example `[{{status}}] {{rule_name}}`). Unknown
  placeholders are rejected when you save.
- **Body (resolved)**, optional. Used for resolved notifications. Empty reuses
  the fired body.
- **Per-channel bodies**, optional. Replace the fired body for one channel type,
  for example a short text for Telegram and a long one for Email.

## Use it on a rule

In the rule form, pick the template under **Notification template**. The
picker appears once at least one template exists. Text you write under
**Customize the notification message** still overrides the template, field by
field.

## Set a default

Turn on **Use as the default template** for one template. It applies to every
rule that picks none. Only one template can be the default. With no default and
no template picked, rules keep each channel's built-in wording.

## Delete a template

A template that rules still use can't be deleted. The error lists the rules;
pick another template on them first.

## Export and import

`flare alerts export` records a rule's template by name, and `import` looks the
name up on the target instance. Create the template there first, or the rule is
reported as an error.

The API is `/api/alert-templates`. See
[ADR-0148](../../docs-internal/adr/0148-shared-alert-notification-templates.md) for
the design.
