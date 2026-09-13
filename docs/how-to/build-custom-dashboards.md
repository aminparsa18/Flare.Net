# How to build a custom dashboard

Compose a **multi-panel dashboard** out of queries you already built on the
Logs, Traces, and Metrics pages — one saved object with several widgets on
it, instead of several separate saved searches you have to open one at a
time. For the storage/execution design behind this, see
[the original ADR](../../docs-internal/adr/0023-custom-dashboards.md) and
[the editor follow-up](../../docs-internal/adr/0024-custom-dashboards-phase2-editor.md);
for the single-page equivalent (one saved filter, no panels), see
[Saved searches](#saved-searches-vs-dashboards) below.

## Prerequisites

- A running Flare instance you can reach in a browser (any of
  [standalone](run-standalone.md), [Aspire](run-with-aspire.md), or the
  [CLI](run-with-cli.md)) with some data already flowing in.

## Steps

There are two ways to get a panel onto a dashboard:

**From an Explorer page** (Logs, Traces, or Metrics):

1. Build the query you want as a panel — a search term, service filter, time
   range, or (on Metrics) a specific selected metric.
2. Click **Pin to dashboard** in that page's toolbar.
3. Pick an existing dashboard from the **Dashboard** dropdown, or leave
   **New dashboard…** selected to create one on the spot (name it there).
4. Give the panel a title (it's pre-filled with something sensible — the
   page name, or the selected metric's name on Metrics) and click **Pin**.

**From the dashboard itself**, once it exists:

1. Open the dashboard and click **Edit**.
2. Click **Add panel**, pick a panel type (Logs, Traces, or Metrics), build
   the query using the same filter controls as that page's own toolbar, and
   confirm — the live preview underneath shows exactly what will be added.

Repeat from either flow, into the same or a different dashboard, for as many
panels as you want.

Open **Dashboards** in the top nav to see every dashboard you've created,
and click **Open** on one to view all its panels together, each showing
live data.

## Editing a dashboard's layout

Click **Edit** on a dashboard to:

- **Drag** a panel (by its grip handle) to reposition it, and **resize** it
  from its corner/edge, on a 12-column grid.
- **Rename** a panel — click its title and type a new one.
- **Remove** a panel (the trash icon in its header).
- **Add** a new panel in place, per the steps above.

Every change saves immediately — there's no separate "Save" step. Click
**Done editing** to leave edit mode and lock the layout again (view mode
never risks an accidental drag).

## Overriding the time range for a session

The **time range** picker in a dashboard's header (next to Edit) lets you
temporarily view every panel over the same fixed window — "Last 1 hour",
"Last 24 hours", and so on — regardless of what each panel was individually
pinned/added with. Set it back to **Each panel's own range** (or just reload
the page) to go back to each panel showing whatever range it was saved
with. This override is per-browser-session only: it's never saved to the
dashboard, so it never changes what anyone else sees when they open it.

## Managing dashboards

From the **Dashboards** page you can:

- **Create** a blank dashboard (then add panels to it, per the steps above).
- **Rename** a dashboard's name or description.
- **Delete** a dashboard. This only removes the dashboard object itself —
  it never touches the underlying Logs/Traces/Metrics data.

## Verification

Open a dashboard with at least one panel on it and confirm each panel is
showing real, current data (not stuck loading or empty) — a Logs panel
shows its usual event-volume chart, a Metrics panel its usual line/
percentile chart, and a Traces panel its usual trace list, exactly as they
look on their own page. Then click **Edit** and confirm a drag/resize
sticks after leaving edit mode and reloading the page.

## Saved searches vs. dashboards

Both start from the same place — a query you built on Logs/Traces/Metrics —
but they solve different problems:

| | Saved search (**Views**) | Dashboard |
|---|---|---|
| Scope | One page's filter state | Several panels, any mix of pages |
| Reopening it | Replaces the current page's filter | Shows every panel side by side |
| Best for | "Get back to this exact search fast" | "See several things at a glance" |

Nothing stops you from having both a saved search and a dashboard panel for
the same query — pinning/adding a panel doesn't consume or remove a saved
search.

## Known gaps, stated plainly

- **No auto-refresh** — reload the page to see newer data.
- **No duplicate/export** for a dashboard or a single panel.
- **Dashboards are visible to every signed-in user**, the same as saved
  searches and alert rules today — there's no per-user ownership or private
  dashboards yet.

None of these are permanent limits, just not built yet.
