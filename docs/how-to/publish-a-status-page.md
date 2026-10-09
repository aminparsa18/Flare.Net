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

## Manage pages from the CLI or Terraform

Both need an Admin.

```bash
flare status-pages create acme-public --title 'Acme status' \
  --component 'API=slo:<slo-id>' --component 'Website=monitor:<monitor-id>' --enabled true
flare status-pages list
flare status-pages update <id> --enabled false
```

`update` changes only the options you pass; `--component` replaces all components.

With the Flare Terraform / OpenTofu provider:

```hcl
resource "flare_status_page" "public" {
  slug    = "acme-public"
  title   = "Acme status"
  enabled = true
  components = [
    { name = "API", kind = "Slo", ref_id = flare_slo.api.id },
  ]
  subscriber_channel_ids = [flare_notification_channel.oncall.id]
}
```

## Post incidents

Computed health says what is up; an incident says why, in your words. In **Settings > Status pages**, open a page's **Incidents** dialog, report an incident with a title, a status (Investigating, Identified, Monitoring or Resolved) and a message, then post further updates as it progresses. Posting a **Resolved** update closes it.

Open incidents show above the components on the public page, and resolved ones stay for 14 days. Incidents do not change the computed state or the banner. The API is `/api/status-pages/{id}/incidents`; update text is public, so keep secrets out of it. Tick the components an incident affects and the public page names them next to it; the `components` field of the API takes their monitor or SLO ids, and a later update can change the list. It is a label only: the component's computed state is unchanged.

### Notify channels

A page can tell your own channels when an incident is opened or updated. In the page editor, add channels under **Incident notifications**, or pass `--subscriber <channel-id>` (repeatable) to `flare status-pages create` or `update`; the API field is `subscriberChannelIds`. Each update is sent as a message with the page, the status, the incident title, your text, the affected components and a link to the public page. Only webhook (including Slack), Telegram, email, Teams and Discord channels qualify; a delivery failure is logged and never blocks posting the update. The link needs `Alerting__PublicUrl` set. These are your channels, not a sign-up form for visitors.

### Let visitors subscribe by email

When the server can send email (`Email__Host`, `Email__From`) and knows its public address (`Alerting__PublicUrl`), the public page shows a **Get updates by email** form. A visitor enters an address, gets a confirmation link, and after confirming receives an email for every incident opened or updated on that page. Every email carries an unsubscribe link. Without that configuration the form is hidden. Visitors can also tick the components they care about; they then only get incidents that affect one of them, plus any incident that names no component. A verified address cannot be changed from the public form (otherwise anyone who knew an address could narrow that person's alerts); instead, on a page with more than one component, every incident email carries a **Choose which components you hear about** link that opens a page where the subscriber ticks components and saves.

An unconfirmed address is mailed again at most every 10 minutes, a page keeps up to 2,000 subscribers, and sign-up requests are limited to 30 per hour per caller address (behind a reverse proxy that is the proxy's address). The form gives the same answer whether or not an address is already subscribed. Admins list a page's subscribers with `GET /api/status-pages/{id}/subscribers` and remove one with `DELETE /api/status-pages/{id}/subscribers/{subscriberId}`.

### Brand the page and serve it on your own domain

Each page can have a **logo** (`logoUrl`, an https image), an **accent color** (`accentColor`, `#rrggbb`), a **support link** (`supportUrl`, https or `mailto:`), shown as "Contact support", and a **custom domain** (`domain`, e.g. `status.example.com`). Set them in the page editor, or with `--logo-url`, `--accent-color`, `--support-url` and `--domain` on `flare status-pages create` and `update`; an empty value clears a field and leaving it out keeps it.

Flare does not issue certificates or manage DNS. Point the host's DNS and TLS (your reverse proxy or load balancer) at the dashboard, then set the domain on the page. That host serves only the page: `/` redirects to `/status/<slug>`, and everything else, including the rest of the dashboard, returns 404. Do not use the dashboard's own host as a page domain. Each domain belongs to one page. If the dashboard reaches the API at a different address from browsers, set `API_INTERNAL_URL` on the dashboard. Changes can take up to a minute to show. Subscription emails still link to `Alerting__PublicUrl`.

## Things to know

- The page is public to anyone who can reach the instance. Unpublish or delete it to take the link down at once.
- The result is cached for 30 seconds, so a change can take that long to appear. The page refreshes itself every minute.
- Renaming a monitor starts its history over, because probe results are stored under the monitor's name.
- A component whose monitor or SLO was deleted stays on the page as **No data**; remove it in the editor.
