# How to publish a status page

A **status page** is a read-only page of service health that anyone with the link can open, with no Flare account. It shows each component's current state and its daily uptime for the last 90 days. A component is a [synthetic monitor](synthetic-monitoring.md) or an [SLO](define-slos.md), and the page shows only the name you give it.

## Create a page

Open **Settings > Workspace > Status pages** (Admin) and choose **New status page**:

1. Give it a **title** and a **URL slug** (lowercase letters, digits and hyphens). The page is served at `/status/<slug>` on the dashboard.
2. Add **components**: pick monitors and SLOs, and edit the public name each one shows. Monitor targets and SLO names are never shown.
3. Turn on **Published**. Pages start unpublished, and an unpublished page answers 404.

You can do the same with the API (`/api/status-pages`, Admin) using a session cookie or a personal access token:

```bash
curl -X POST "$FLARE_API/api/status-pages" \
  -H "Authorization: Bearer $FLARE_PAT" -H "Content-Type: application/json" \
  -d '{"slug":"status","title":"Acme status","enabled":true,
       "components":[{"name":"Website","kind":"Monitor","refId":"<monitor id>"},
                     {"name":"Checkout API","kind":"Slo","refId":"<slo id>"}]}'
```

`GET`, `PUT` and `DELETE` on `/api/status-pages/{id}` read, change and remove one.

## What visitors see

| State | Monitor component | SLO component |
| --- | --- | --- |
| Operational | Every location that reported recently sees it up. | The error budget is not spent. |
| Degraded | Some locations see it down. | The error budget is overspent. |
| Outage | Every location sees it down. | |
| No data | Disabled, never probed, or no result for three intervals. | No traffic in the SLO window. |

The banner at the top shows the worst state among the components. Each component also has a bar of up to 90 days, one segment per UTC day, green at 99.9% or more, amber at 95% or more and red below that. A monitor's day is the share of probes that were up. An SLO's day is the share of good events, and an SLO shows only as many days as its own window.

## Things to know

- The page is public to anyone who can reach the instance. Unpublish or delete it to take the link down at once.
- The result is cached for 30 seconds, so a change can take that long to appear. The page refreshes itself every minute.
- Renaming a monitor starts its history over, because probe results are stored under the monitor's name.
- A component whose monitor or SLO was deleted stays on the page as **No data**; remove it in the editor.
