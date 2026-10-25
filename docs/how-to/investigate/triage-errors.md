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

If the group's service has a GitHub or GitLab repository configured for [stack-trace source links](link-exceptions-to-source-code.md), the menu also offers **New GitHub issue** or **New GitLab issue**. It opens the host's new-issue page with the title and a short description (exception, services, counts and a link back to Flare) already filled in. Flare doesn't call the host itself, so you file the issue as yourself and then paste its URL into **Link issue…**.

For Jira, an admin configures the service once: open an occurrence of one of its errors and use the ticket icon next to the source-link icon to enter the Jira site URL (for example `https://acme.atlassian.net`), the numeric **project id** and, optionally, the numeric **issue type id**. The project id is the number in the project's settings URL, or the `id` returned by `/rest/api/2/project/<KEY>` on your site. The menu then offers **New Jira issue**, which opens Jira's create page with the summary and description filled in. As with GitHub and GitLab, Flare doesn't call Jira, so you file the issue as yourself and paste its URL into **Link issue…**.

## Assign owners automatically

Click **Ownership rules** in the toolbar to map errors to people. Each rule matches either a **service** name or an **exception type** with a case-insensitive pattern where `*` stands for any text, for example `payments-*` or `*Timeout*`, and names the owner's username. Rules are checked top to bottom and the first match owns the group, shown as "Owner alice (rule)". Use the arrows to reorder them.

A third kind, **File path**, matches the source files in the group's stack trace using [CODEOWNERS](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners) path syntax: `/src/payments/` (a directory at the repo root), `*.sql` (any depth), `docs/*.md`, `src/**/Charge.cs`. Flare reads the file paths from one sample stack trace per group, so only frames that carry file information count (for .NET that means symbols, or a deterministic build with source paths). The checkout location on the build machine doesn't matter, because a pattern is tried against every trailing part of each path.

**Import CODEOWNERS** turns a pasted CODEOWNERS file into File path rules. Each line takes its first owner with the leading `@` dropped, and the order is reversed because CODEOWNERS lets later lines win while Flare uses the first match. Owners are only useful if they equal Flare usernames, so review the list before saving.

A person set with **Assign to me** always wins over a rule. Rules are not applied retroactively as stored assignments: change a rule and the owner shown changes with it.

## Regressions

A resolved group that occurs again in a `service.version` it had not been seen in is shown as **Regressed**, with the version. An occurrence in a version the group was already seen in does not count: the fix has probably not reached that instance yet. If your services don't report `service.version`, any new occurrence of a resolved group counts as a regression.

## Filter by status

The status filter next to the service filter defaults to **Active**, which hides ignored groups. Pick **All statuses**, or a single status, to see the rest.

**My errors** next to it shows only the groups you own: assigned to you, or, with no assignee, matched to you by an ownership rule. The button is hidden when sign-in is off, because there is no current user. The choice is remembered in this browser, and `/errors?mine=1` opens the page with it on.

**My errors** next to it shows only the groups you own: assigned to you, or, with no assignee, matched to you by an ownership rule. The button is hidden when sign-in is off, because there is no current user. The choice is remembered in this browser, and `/errors?mine=1` opens the page with it on.

## Alerts

Exception-count alert rules don't count ignored groups, and the incident summary leaves them out. The change applies from the rule's next evaluation. When an ignore lapses, the group counts again.

An exception-count notification also names who owns the error. It looks at the rule's three most frequent groups in the window and adds an `Owner:` line with each group's assignee or, failing that, the first ownership rule that matches (service, exception type or stack-trace path), without repeating a name. The same value is available as `{{owner}}` in [notification templates](../alerting/share-alert-notification-templates.md), empty when nobody owns the groups, and the AI incident summary gets it too.

An exception-count notification also names who owns the error. It looks at the rule's three most frequent groups in the window and adds an `Owner:` line with each group's assignee or, failing that, the first ownership rule that matches (service, exception type or stack-trace path), without repeating a name. The same value is available as `{{owner}}` in [notification templates](../alerting/share-alert-notification-templates.md), empty when nobody owns the groups, and the AI incident summary gets it too.

## Change how errors are grouped

Click **Grouping rules** on the Errors page (Member or Admin). There are three controls, applied in this order:

1. **Collapse ids, numbers and quoted values** is the automatic normalization above. Turn it off if it merges errors you want apart, such as `HTTP 404` and `HTTP 500`.
2. **Replace** rules rewrite part of every message with a regular expression ([RE2 syntax](https://github.com/google/re2/wiki/Syntax)) and a replacement, where `\1` is the first capture group. `user [a-z]+` replaced by `user <name>` makes `user alice denied` and `user bob denied` one group. Rules run in list order, after the automatic normalization, on every message regardless of exception type.
3. **Merge** folds one group into another of the same exception type. Open a row's menu, choose **Merge into another group…** and pick the target. The merge is saved as a rule, so it also applies to future occurrences, and appears in the same dialog.

Saving checks every expression. A pattern that does not compile, matches an empty string, or whose replacement matches the pattern again is rejected with the reason.

Triage state follows the groups. When rules change, resolved, ignored and assigned state moves to the group its message now belongs to; when several groups become one, the most recently changed one wins. Taking a rule away, or turning the normalization off, separates groups again but does not bring back state a merged-away group had: those groups start Open.

## Limits

Groups are a type plus a normalized message. Before grouping, Flare replaces the variable parts of a message with placeholders: GUIDs (`<guid>`), `0x` hex values (`<hex>`), ISO-8601 timestamps (`<ts>`), quoted values (`'<s>'`) and standalone numbers (`<n>`). `Order 4812 not found` and `Order 77 not found` are one group, shown as `Order <n> not found`; open the group to see each occurrence's own message. Identifiers that merely contain digits, such as `HTTP2` or `sha256`, are left alone.

A message whose variable part is a word rather than a number, id or quoted value (a user name, a host name) is not collapsed automatically; see [Change how errors are grouped](#change-how-errors-are-grouped).

Triage state follows the normalized group. Upgrading re-keys state saved under the old exact messages (migration `0087`): when several old groups collapse into one, the most recently changed one wins. Exception-count alert rules match the same way, so a rule whose message is `Order 4812 not found` now also covers the other order ids.
