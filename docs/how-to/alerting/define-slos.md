# How to define SLOs and get alerted when the error budget burns

A service level objective (SLO) is a reliability promise measured over a rolling window: for example, "99.5% of `POST /checkout` requests succeed over 28 days". The 0.5% that may fail is the **error budget**. Flare shows how much of it is left and alerts you when it is being spent too fast, before it runs out.

## Define an SLO

1. Open **SLOs** from the user menu, then **New SLO**.
2. Pick the objective:
   - **Availability**: a request is good unless its span ended in an error.
   - **Latency**: a request is good when it finishes within a threshold. The threshold is one of 50, 100, 250, 500, 1000, 2500, 5000 or 10000 ms.
3. Pick the service, and optionally one endpoint (an entry span name such as a route). With no endpoint, every entry span of the service counts.
4. Set the target (for example 99.5) and the window (7 to 90 days, 28 by default).

A "request" is an entry span: a server or consumer span, or a root span for work that starts its own trace. Client and internal spans are not counted.

## Read the budget

The list shows each SLO's current SLI (the percentage of good requests), the share of its error budget left, and its burn rate over the last hour. Open one for the budget over the window and the burn rate over 5 minutes, 30 minutes, 1 hour, 6 hours and 24 hours.

A **burn rate** of 1x spends exactly the whole budget by the end of the window. 14.4x spends 2% of a 30-day budget in one hour. A window with no requests shows `-`: no traffic is neither a breach nor a healthy 100%.

## Alert on the burn rate

In an SLO's drill-down, pick one or more notification channels and choose **Create burn-rate alerts**. Flare creates two rules, scaled to the SLO's window:

| Rule | Fires when | Severity | For a 30-day window |
| --- | --- | --- | --- |
| Fast burn | 2% of the budget burns in 1 hour, confirmed over the last 5 minutes | Critical | burn rate of 14.4x or more |
| Slow burn | 5% of the budget burns in 6 hours, confirmed over the last 30 minutes | Warning | burn rate of 6x or more |

A rule fires only when the burn rate is at or above its threshold over **both** windows. The long window shows the burn is sustained, the short one that it is still happening, so the alert resolves soon after the problem stops.

These are ordinary alert rules (condition **SLO burn rate**). They use your notification channels, cooldown, maintenance windows and resolved notifications like any other rule, and appear on the Alerts page. To change one, delete it and create it again from the SLO page. Creating the pair again only adds a rule that is missing.

## Back-fill history

SLOs read a per-minute pre-aggregate that is filled as spans are ingested. After upgrading, it only holds spans received since the upgrade, so a long window starts short. To fill it from the spans you already have, run the `INSERT ... SELECT` in the comment at the top of `db/clickhouse/0049_slos.sql` once.

## Limits

- Latency is counted over every request, errors included. A fast error counts as good for latency, so pair a latency SLO with an availability SLO for the same endpoint.
- Name your spans with route templates (`GET /orders/{id}`), not concrete URLs: each distinct span name is its own endpoint.
- Deleting an SLO keeps its burn-rate rules, but they stop evaluating. Delete them from the SLO page first.
- The API is `GET/POST /api/slos`, `GET/PUT/DELETE /api/slos/{id}` and `GET /api/slos/{id}/status`. Reads need any signed-in user; writes need Member or Admin.
