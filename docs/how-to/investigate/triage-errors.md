# How to triage errors

The **Errors** page groups exceptions by type and message. You can mark each group as resolved or ignored, assign it to someone, and have Flare tell you when a "fixed" error comes back.

## Change a group's status

Open the **⋯** menu at the end of a row:

- **Mark resolved**: you believe it is fixed. Flare notes the `service.version` values the group has been seen in over the last 30 days.
- **Ignore**: hide the group and stop exception-count alert rules from counting it. Choose how long: until you reopen it, for 24 hours, for 7 days, or until 100 more occurrences.
- **Reopen**: back to Open.
- **Assign to me** / **Unassign**: record who is looking at it.

Changing status needs the Member or Admin role. Viewers see the status but not the menu.

## Regressions

A resolved group that occurs again in a `service.version` it had not been seen in is shown as **Regressed**, with the version. An occurrence in a version the group was already seen in does not count: the fix has probably not reached that instance yet. If your services don't report `service.version`, any new occurrence of a resolved group counts as a regression.

## Filter by status

The status filter next to the service filter defaults to **Active**, which hides ignored groups. Pick **All statuses**, or a single status, to see the rest.

## Alerts

Exception-count alert rules don't count ignored groups, and the incident summary leaves them out. The change applies from the rule's next evaluation. When an ignore lapses, the group counts again.

## Limits

Groups are exact type-and-message pairs. An error whose message contains a changing id appears as a new group each time, and starts Open.
