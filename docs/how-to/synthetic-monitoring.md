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
| `kind` | `Http`, `Tcp` or `Tls`. |
| `target` | Http: an absolute `http(s)` URL. Tcp: `host:port`. Tls: `host` or `host:port` (443 by default). |
| `method` | Http only: `GET` (default), `HEAD`, `POST` or `OPTIONS`. |
| `expectedStatus` | Http only: the status that counts as up. `0` (default) means any 2xx or 3xx. |
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

## Alert on a monitor

Create a normal metric alert rule on one of those metrics, filtered by the `monitor` attribute:

- `synthetic.up` with **Min** below 1 over 3 minutes: the endpoint was down.
- `synthetic.duration` above 2000 over 5 minutes: it is slow.
- `synthetic.cert.expiry_days` below 14: renew the certificate.
- **Absent data** on `synthetic.up`: the monitor itself stopped reporting.

## Limits

- Probes run from where `Flare.AlertWorker` runs, so they test reachability from that one place.
- A monitor makes the server send requests to its target, including internal hosts. Set `Synthetic__Enabled=false` on the worker to turn probing off.
- `Synthetic__PollInterval` (5 s) and `Synthetic__MaxConcurrency` (20) tune the runner.
