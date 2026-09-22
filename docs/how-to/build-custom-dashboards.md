# How to build a custom dashboard

Compose a **multi-panel dashboard** out of queries you already built on the
Logs, Traces, and Metrics pages — one saved object with several widgets on
it, instead of several separate saved searches you have to open one at a
time. For the storage/execution design behind this, see
[the original ADR](../../docs-internal/adr/0023-custom-dashboards.md),
[the editor follow-up](../../docs-internal/adr/0024-custom-dashboards-phase2-editor.md),
and [the variables follow-up](../../docs-internal/adr/0025-dashboard-variables.md);
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
   range, or (on Metrics) a specific selected metric, or a Formula-mode
   expression combining several named metric queries (e.g. `A / B`).
2. Click **Pin to dashboard** in that page's toolbar.
3. Pick an existing dashboard from the **Dashboard** dropdown, or leave
   **New dashboard…** selected to create one on the spot (name it there).
4. Give the panel a title (it's pre-filled with something sensible — the
   page name, the selected metric's name, or the formula expression itself
   on Metrics) and click **Pin**.

**From the dashboard itself**, once it exists:

1. Open the dashboard and click **Edit**.
2. Click **Add panel**, pick a panel type (Logs, Traces, or Metrics), build
   the query using the same filter controls as that page's own toolbar —
   including the Single metric/Formula mode toggle on Metrics — and
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
- **Duplicate** a panel (the copy icon in its header) — adds an independent
  copy, named "*(copy)*", below the rest of the dashboard's panels, that you
  can then edit or reposition separately.
- **Remove** a panel (the trash icon in its header).
- **Add** a new panel in place, per the steps above.

Independent of edit mode, every panel also has an **export** icon in its
header that downloads that one panel's type, title, size, and query as a
JSON file — the per-panel equivalent of the whole-dashboard Export described
under "Managing dashboards" below, useful as a smaller snapshot when you
only care about one panel's definition. There's no per-panel import — bring
the query back by hand (Add panel, same type, matching filters).

Every change saves immediately — there's no separate "Save" step. Click
**Done editing** to leave edit mode and lock the layout again (view mode
never risks an accidental drag).

## Setting a Y-axis range on a Metrics panel

In edit mode, a Metrics panel's header also has an **up-down arrow** icon —
set a **min** and/or **max** to pin the chart's Y-axis instead of letting it
auto-range to the data's own peak (useful to keep a mostly-flat metric, like
CPU pinned near 0%, from looking noisier than it is, or to align two panels
charting the same unit on the same scale). Leave either field blank to keep
that side auto-ranged. This is a *soft* bound: the axis still expands past a
set value if the data actually goes further — it narrows the default view,
it never clips a real point off the chart. Like the rest of a panel's
definition, it's saved with the dashboard.

## Overriding the time range for a session

The **time range** picker in a dashboard's header (next to Edit) lets you
temporarily view every panel over the same fixed window — "Last 1 hour",
"Last 24 hours", and so on — regardless of what each panel was individually
pinned/added with. Set it back to **Each panel's own range** (or just reload
the page) to go back to each panel showing whatever range it was saved
with. This override is per-browser-session only: it's never saved to the
dashboard, so it never changes what anyone else sees when they open it.

## Auto-refreshing a dashboard

The **refresh** picker in a dashboard's header (next to the time-range
override) re-runs every panel's query on a fixed interval — 15s, 30s, 1m, or
5m — instead of you reloading the page to see newer data. Set it back to
**Auto-refresh off** to stop. Like the time-range override, this is
per-browser-session only: it resets when you leave the page, and it's never
saved to the dashboard.

## Setting a dashboard as your home page

Click the **home** icon in a dashboard's header to make it the page you land
on instead of the Logs Explorer whenever you open Flare at its root URL (or
click the Flare logo). Click it again to unset it. This is a per-browser
preference (not saved to the dashboard object, so it doesn't affect what
anyone else sees) — if the dashboard is later deleted, Flare falls back to
the Logs Explorer next time rather than showing a broken page.

## Dashboard variables

Click **Variables** in a dashboard's header (in edit mode) to define
dropdowns that temporarily narrow every panel that can use them, for this
browser session — regardless of what each panel was individually
pinned/added with. Unlike the time-range/auto-refresh/service overrides,
which are fixed built-in controls, a variable is something you define
yourself, and any number of them can exist on one dashboard:

1. Click **Add variable** and give it a **name** (shown as its dropdown's
   label in the header).
2. Choose what it **backs**:
   - **Service** — narrows every Logs/Traces/Metrics panel's service
     filter, the same thing Phase 4's old fixed Service override did.
   - **Attribute** — narrows Logs and/or Traces panels (Metrics has no
     attribute filter to narrow) to one attribute value. Pick an
     **attribute type** — Log attribute (Logs panels only), Span attribute
     (Traces panels only), or Resource/Scope attribute (both, since those
     are the same ingest-time-correlated bag either way) — and type the
     **attribute key** (e.g. `http.method`).
3. Choose where its **values** come from:
   - **From query** — every distinct value observed over the last 7 days,
     resolved automatically (the same lookup Logs'/Traces' own attribute
     filter builders already use for autocomplete).
   - **Custom list** — a fixed, comma-separated list you type in yourself.
4. Optionally set a **default value**, preselected whenever the dashboard
   is opened. Leave it blank for "All" (the variable doesn't narrow
   anything until you pick a value).
5. Optionally set **Depends on** (only shown for "From query" variables) to
   chain this variable off another one already defined on the dashboard —
   see "Variable chaining" below.

Each variable then gets its own dropdown next to the time-range/refresh
controls. Selecting a value narrows every panel its target/attribute type
applies to; more than one Service-backed variable combines as "any of
these services" rather than one replacing another. An attribute variable's
value is *added to* a panel's own saved filters, not a replacement — a
panel that already filters on, say, an error level keeps that filter too.
Like the time-range override, a variable's *selected value* is
per-browser-session only and never saved to the dashboard — only its
*definition* (name, target, source) is, via **Variables**, so everyone who
opens the dashboard sees the same dropdowns but can pick their own values.

Each panel can also individually opt out of a variable — see "Per-panel
opt-out" below.

### Variable chaining

A "From query" variable can optionally **depend on** another variable
already defined on the dashboard: pick one in its **Depends on** dropdown
(only offered for other variables that wouldn't create a dependency
cycle). Once chained, its own value list is resolved narrowed by whichever
value its parent is *currently* set to, instead of the unfiltered 7-day
window every independent variable's values are drawn from — for example, a
"Host" variable backing a Resource attribute can depend on a "Service"
variable, so its dropdown only offers hosts actually seen for the
currently-selected service, not every host across every service. Picking
"All" on the parent (or leaving a chained variable's own "Depends on" one
without ever picking a parent value) falls back to the same unfiltered
window a variable with no dependency uses. Changing a parent's selected
value re-resolves every variable chained off it (and, transitively,
anything chained off *those*) automatically; if a dependent's own current
selection is no longer among its freshly-resolved options, it resets to
"All" rather than silently keep narrowing panels by a value that's no
longer actually offered. Like every other variable relationship, only the
*chain itself* (which variable depends on which) is part of the saved
dashboard — which values are currently selected stays session-only, same
as an unchained variable's own selection.

### Per-panel opt-out

A variable narrows every panel its target/attribute type applies to by
default. To exclude one specific panel from one specific variable, open
**Edit** mode and click the **filter** icon in that panel's own header
(only shown once the dashboard has at least one variable) — it lists
every variable with a checkbox, checked meaning "this variable narrows
this panel." Uncheck one to stop it narrowing that panel; every other
panel keeps being narrowed by it as normal. Like the rest of a panel's
definition (title, size, query), which variables a panel opts out of is
saved with the dashboard — unlike a variable's own *selected value*,
which stays session-only.

## Creating an alert from a panel

Click the **bell** icon in a Logs or Metrics panel's header to open the
**Create alert** dialog on the Alerts page, pre-filled with that panel's
condition — a Logs panel's service/severity/search filter, or a Metrics
panel's selected metric. Adjust the threshold, window, and notification
channel, then save as usual. Traces panels don't offer this — there's no
trace-based alert condition today (alert rules only support log-count,
metric-threshold, and exception-count conditions).

## Full-screen / TV mode

Click the **full screen** icon in a dashboard's header (next to Edit) to
hide Flare's navigation bar and fill the browser window with just the
dashboard's panels — useful for a wall-mounted display or a TV in a team
space. Click the icon again, or press **Escape**, to exit. This uses your
browser's own full-screen mode, so it's per-browser/per-session like the
time-range override and auto-refresh above — nothing about it is saved to
the dashboard.

## Managing dashboards

Renaming, editing, or deleting a dashboard requires being that dashboard's
own creator or an Admin — anyone else with Member access can still view,
duplicate, or export it, just not change it in place (the "Pin to
dashboard" action on a Logs/Traces/Metrics panel only offers dashboards you
can actually update, for the same reason — pick "New dashboard" instead to
pin into one of your own). A dashboard created before this rule existed (or
on an instance with auth disabled entirely) has no owner recorded, so any
Member can still change it, same as before.

From the **Dashboards** page you can:

- **Create** a blank dashboard (then add panels to it, per the steps above).
- **Rename** a dashboard's name or description.
- **Duplicate** a dashboard — creates an independent copy with the same
  panels, named "*(copy)*", that you can then edit separately.
- **Export** a dashboard — downloads its name, description, and panel
  definitions as a JSON file, a readable backup/snapshot you can also use
  to move a dashboard to another Flare instance (see **Import** below).
- **Import** a dashboard — pick a JSON file to create a new dashboard from
  it. Two shapes are recognized from the same file picker:
  - A Flare export (previously produced by Export, on this instance or
    another one) — panels come back exactly as exported, queries included.
  - A Grafana dashboard export ("Export as JSON" from Grafana's own UI, or
    the `dashboard` field of its `GET /api/dashboards/uid/:uid` response) —
    layout (panel position/size) and titles carry over, and each panel's
    type maps to the closest of Flare's three (time-series/stat/gauge-style
    panels → Metrics, logs/table panels → Logs, trace panels → Traces).
    **Queries don't** — a Grafana panel's query is written against whatever
    datasource it points at (PromQL, LogQL, ...), which has no equivalent in
    Flare's own log/trace/metric query shapes, so every imported panel's
    query is reset to a blank default and needs configuring afterward (open
    the panel and set what it shows, same as a brand-new panel). A panel
    type with no Flare equivalent (text, heatmap, node graph, ...) is
    skipped rather than guessed at; the import summary says how many panels
    came in and how many were skipped, and why.

  An invalid or unrelated file (neither shape) is rejected with an inline
  error rather than partially imported.
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

- **Grafana import is structural only** — layout and panel type come over,
  queries don't (see "Managing dashboards" above for why). There's no plan
  to build real query translation; the datasources don't correspond.
- **No per-panel import** — duplicate and export work per-panel (see
  "Editing a dashboard's layout" above), but a panel's exported JSON can't
  be read back in; only a whole dashboard's export/import round-trips.
- **Dashboards are visible to every signed-in user**, the same as saved
  searches and alert rules — there's no private/shared-only dashboards yet.
  (The "set as home page" preference above is per-browser, not per-user,
  and doesn't change who can see the dashboard itself.) *Who may change* a
  given dashboard is narrower, though — see "Managing dashboards" below.

None of these are permanent limits, just not built yet.
