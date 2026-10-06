# How to monitor endpoints, ports and certificates with synthetic probes

Applications report on themselves, which does not help when an endpoint is unreachable or a certificate is about to expire. A **synthetic monitor** is a probe Flare runs on a schedule from the outside. Its result is stored as ordinary metrics, so you alert on it and chart it the way you do any other metric.

## Create a monitor

Open **Settings > Workspace > Synthetic monitors** and choose **New monitor**, or use the API (`POST /api/synthetic-monitors`, Member or Admin) with a session cookie or a personal access token:

```bash
curl -X POST "$FLARE_API/api/synthetic-monitors" \
  -H "Authorization: Bearer $FLARE_PAT" -H "Content-Type: application/json" \
  -d '{"name":"checkout health","kind":"Http","target":"https://shop.example.com/health","intervalSeconds":60}'
```

| Field | Meaning |
| --- | --- |
| `kind` | `Http`, `Tcp`, `Tls`, `Dns`, `Udp` or `Icmp`. |
| `target` | Http: an absolute `http(s)` URL. Tcp: `host:port`. Tls: `host` or `host:port` (443 by default). Dns, Icmp: a host name or IP address. Udp: `host:port`. |
| `method` | Http only: `GET` (default), `HEAD`, `POST` or `OPTIONS`. |
| `expectedStatus` | Http only: the status that counts as up. `0` (default) means any 2xx or 3xx. |
| `requestHeaders` | Http only: request headers, one `Name: value` per line. The API never returns the values: they read back as `********`, and sending `Name: ********` on an update keeps the stored value. |
| `requestBody` | Http only, `POST` only: the request body. A `Content-Type` header sets its type. |
| `expectedAnswer` | Dns: text the answer must include (`method` is then the record type: `A` by default, `AAAA`, `MX`, `TXT` or `CNAME`; for `A`/`AAAA` it must be an IP address; MX answers read `10 mail.example.com`; MX, TXT and CNAME are queried directly from the worker's configured name servers). Udp: text the reply must contain; `requestBody` is the datagram sent. |
| `bodyContains` / `bodyNotContains` | Http only: the response body must contain / must not contain this text (case-sensitive; only the first 1 MiB is read). A failed assertion records `synthetic.up` as 0. |
| `bodyMatchesRegex` | Http only: the response body must match this regular expression. It runs on a linear-time engine, so lookarounds and backreferences are rejected. |
| `jsonPath` / `jsonPathEquals` | Http only: a path such as `$.data.items[0].status` or `$['key']` that must exist in the JSON response, and optionally equal `jsonPathEquals` (strings unquoted, other values as JSON text). |
| `intervalSeconds` | 10 to 86400, 60 by default. |
| `timeoutSeconds` | 1 to 120, 10 by default, not above the interval. |
| `enabled` | `false` pauses the monitor. |

`GET`, `PUT` and `DELETE` on `/api/synthetic-monitors/{id}` read, change and remove one.

## What is recorded

Every probe writes gauge metrics for service `flare-synthetic`, each with the attributes `monitor` (the name), `kind` and `target`:

| Metric | Value |
| --- | --- |
| `synthetic.up` | 1 when the probe succeeded, 0 otherwise. |
| `synthetic.duration` | Time to the response, connection or handshake, in ms. |
| `synthetic.http.status_code` | Http only: the status received. |
| `synthetic.cert.expiry_days` | Tls only: days until the certificate expires. |

A timeout, connection error, failed TLS handshake (an expired, untrusted or mismatched certificate fails it) or unexpected status all record `synthetic.up` as 0.

The monitors table shows each monitor's latest result (up or down, with the probe time), and `flare synthetic-monitors list` shows the same from the terminal. `create`, `update` and `delete` are there too; see the [CLI reference](../reference/cli-commands.md).

## Probe from several locations

Each `Flare.AlertWorker` names the place it probes from with `Synthetic__Location` (default `default`). Run a worker in each region with its own name, all pointing at the same ClickHouse and Redis, for example `Synthetic__Location=eu-west` in one and `us-east` in another.

A monitor's **Probe locations** field (or `flare synthetic-monitors create ... --location eu-west --location us-east`) lists the locations that run it; leave it empty to run it from every worker. Every location probes once per interval, and every result carries a `location` attribute. The table shows a badge per location when more than one has reported.

An alert on `synthetic.up` with **Min** below 1 fires when any location sees the monitor down. To alert on a quorum of locations, use **Last** instead. Each location is its own series, and **Last** averages every series' latest result, so for `synthetic.up` it is the fraction of locations that see the monitor up:

| Fire when | Aggregation and threshold |
|-----------|---------------------------|
| any location is down | **Min** below 1 |
| at least half of the locations are down | **Last** below 0.51 |
| every location is down | **Last** below 0.01 |

With four locations, **Last** below 0.76 means two or more are down. Pick the threshold between the fractions you want to tell apart.

You don't have to work these out: the bell button on a monitor's row opens the alert form filtered to that monitor, with a **Down from at least N of M locations** picker that sets **Last** and the threshold for you.

## Alert on a monitor

Create a normal metric alert rule on one of those metrics, filtered by the `monitor` attribute:

- `synthetic.up` with **Min** below 1 over 3 minutes: the endpoint was down.
- `synthetic.duration` above 2000 over 5 minutes: it is slow.
- `synthetic.cert.expiry_days` below 14: renew the certificate.
- **Absent data** on `synthetic.up`: the monitor itself stopped reporting.

## Limits

- Probes run from where `Flare.AlertWorker` runs. To probe from more than one place, see [Probe from several locations](#probe-from-several-locations).
- A monitor makes the server send requests to its target, including internal hosts. Set `Synthetic__Enabled=false` on the worker to turn probing off.
- `Synthetic__PollInterval` (5 s) and `Synthetic__MaxConcurrency` (20) tune the runner.
