# How to build a custom dashboard

Compose a **multi-panel dashboard** out of queries you already built on the
Logs, Traces, and Metrics pages — one saved object with several widgets on
it, instead of several separate saved searches you have to open one at a
time. For the storage/execution design behind this, see
[the ADR](../../docs-internal/adr/0023-custom-dashboards.md); for the
single-page equivalent (one saved filter, no panels), see
[Saved searches](#saved-searches-vs-dashboards) below.

## Prerequisites

- A running Flare instance you can reach in a browser (any of
  [standalone](run-standalone.md), [Aspire](run-with-aspire.md), or the
  [CLI](run-with-cli.md)) with some data already flowing in.

## Steps

1. Go to any of **Logs**, **Traces**, or **Metrics** and build the query you
   want as a panel — a search term, service filter, time range, or (on
   Metrics) a specific selected metric.
2. Click **Pin to dashboard** in that page's toolbar.
3. Pick an existing dashboard from the **Dashboard** dropdown, or leave
   **New dashboard…** selected to create one on the spot (name it there).
4. Give the panel a title (it's pre-filled with something sensible — the
   page name, or the selected metric's name on Metrics) and click **Pin**.
5. Repeat from any page, into the same or a different dashboard, for as
   many panels as you want.

Open **Dashboards** in the top nav to see every dashboard you've created,
and click **Open** on one to view all its panels stacked together, each
showing live data.

## Managing dashboards

From the **Dashboards** page you can:

- **Create** a blank dashboard (then pin panels to it from Logs/Traces/
  Metrics, per the steps above) — there's no way to add or configure a
  panel from the dashboard page itself yet.
- **Rename** a dashboard's name or description.
- **Delete** a dashboard. This only removes the dashboard object itself —
  it never touches the underlying Logs/Traces/Metrics data.

From a dashboard's own page, each panel has a **remove** action (the trash
icon in its header) that takes just that one panel off the dashboard.

## Verification

Open a dashboard with at least one panel on it and confirm each panel is
showing real, current data (not stuck loading or empty) — a Logs panel
shows its usual event-volume chart, a Metrics panel its usual line/
percentile chart, and a Traces panel its usual trace list, exactly as they
look on their own page.

## Saved searches vs. dashboards

Both start from the same place — a query you built on Logs/Traces/Metrics —
but they solve different problems:

| | Saved search (**Views**) | Dashboard |
|---|---|---|
| Scope | One page's filter state | Several panels, any mix of pages |
| Reopening it | Replaces the current page's filter | Shows every panel side by side |
| Best for | "Get back to this exact search fast" | "See several things at a glance" |

Nothing stops you from having both a saved search and a dashboard panel for
the same query — pinning a panel doesn't consume or remove a saved search.

## Known gaps, stated plainly

This is the first version of dashboards, and it's intentionally narrow:

- **No drag-to-resize or reordering layout yet** — panels stack in the
  order they were pinned, one per row, all the same size.
- **No dashboard-wide time range** — each panel keeps the time range (and
  every other filter) it had at the moment it was pinned, independently of
  the others. Changing one panel's range means re-pinning it.
- **No auto-refresh** — reload the page to see newer data.
- **Dashboards are visible to every signed-in user**, the same as saved
  searches and alert rules today — there's no per-user ownership or private
  dashboards yet.

None of these are permanent limits, just not built yet.
