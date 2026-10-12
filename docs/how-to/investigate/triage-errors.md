# How to triage errors

The **Errors** page groups exceptions by type and message. You can mark each group as resolved or ignored, assign it to someone, and have Flare tell you when a "fixed" error comes back.

## Change a group's status

Open the **⋯** menu at the end of a row:

- **Mark resolved**: you believe it is fixed. Flare notes the `service.version` values the group has been seen in over the last 30 days.
- **Ignore**: hide the group and stop exception-count alert rules from counting it. Choose how long: until you reopen it, for 24 hours, for 7 days, or until 100 more occurrences.
- **Reopen**: back to Open.
- **Assign to me** / **Unassign**: record who is looking at it.

Changing status needs the Member or Admin role. Viewers see the status but not the menu.

## Link an issue

Open the **⋯** menu and choose **Link issue…** to paste the URL of the issue that tracks the group (GitHub, GitLab, Jira or anything with an http(s) address). The status cell then shows a link such as "Issue #42"; **Remove issue link** clears it.

If the group's service has a GitHub repository configured for [stack-trace source links](link-exceptions-to-source-code.md), the menu also offers **New GitHub issue**. It opens GitHub's new-issue page with the title and a short description (exception, services, counts and a link back to Flare) already filled in. Flare doesn't call GitHub itself, so you file the issue as yourself and then paste its URL into **Link issue…**.

## Assign owners automatically

Click **Ownership rules** in the toolbar to map errors to people. Each rule matches either a **service** name or an **exception type** with a case-insensitive pattern where `*` stands for any text, for example `payments-*` or `*Timeout*`, and names the owner's username. Rules are checked top to bottom and the first match owns the group, shown as "Owner alice (rule)". Use the arrows to reorder them.

A third kind, **File path**, matches the source files in the group's stack trace using [CODEOWNERS](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners) path syntax: `/src/payments/` (a directory at the repo root), `*.sql` (any depth), `docs/*.md`, `src/**/Charge.cs`. Flare reads the file paths from one sample stack trace per group, so only frames that carry file information count (for .NET that means symbols, or a deterministic build with source paths). The checkout location on the build machine doesn't matter, because a pattern is tried against every trailing part of each path.

**Import CODEOWNERS** turns a pasted CODEOWNERS file into File path rules. Each line takes its first owner with the leading `@` dropped, and the order is reversed because CODEOWNERS lets later lines win while Flare uses the first match. Owners are only useful if they equal Flare usernames, so review the list before saving.

A person set with **Assign to me** always wins over a rule. Rules are not applied retroactively as stored assignments: change a rule and the owner shown changes with it.

## Regressions

A resolved group that occurs again in a `service.version` it had not been seen in is shown as **Regressed**, with the version. An occurrence in a version the group was already seen in does not count: the fix has probably not reached that instance yet. If your services don't report `service.version`, any new occurrence of a resolved group counts as a regression.

## Filter by status

The status filter next to the service filter defaults to **Active**, which hides ignored groups. Pick **All statuses**, or a single status, to see the rest.

## Alerts

Exception-count alert rules don't count ignored groups, and the incident summary leaves them out. The change applies from the rule's next evaluation. When an ignore lapses, the group counts again.

## Limits

Groups are exact type-and-message pairs. An error whose message contains a changing id appears as a new group each time, and starts Open.
