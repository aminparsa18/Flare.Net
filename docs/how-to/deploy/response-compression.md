# How to configure API response compression

Flare.Api gzips its responses for any client that sends `Accept-Encoding: gzip`,
which every browser does. A 200-row log page shrinks by about 88%, which makes
the dashboard noticeably faster over a VPN, Wi-Fi or any link slower than a LAN.
This page shows how to tune that, turn it off, or leave it to a reverse proxy.

## What is compressed

- JSON, text and XML responses, the dashboard's MemoryPack responses
  (`application/x-memorypack`), and the CSV and NDJSON exports.
- Over HTTP and HTTPS alike.

These are not compressed:

- WebSocket connections (live tail and the other live views).
- The MCP endpoint's event stream (`text/event-stream`).
- Requests from clients that don't ask for gzip, such as the `flare` CLI.

Compressed responses carry `Content-Encoding: gzip` and `Vary: Accept-Encoding`.

## Change it

| Variable | Default | Meaning |
| --- | --- | --- |
| `ResponseCompression__Enabled` | `true` | `false` turns compression off. |
| `ResponseCompression__Level` | `Fastest` | gzip level: `Fastest` (gzip 1) or `Optimal` (gzip 6). |

Keep `Fastest` unless most users reach Flare over slow links (about 20 Mbps or
less). `Optimal` makes responses 20–40% smaller than `Fastest` but costs more
server CPU per response. On fast links that extra CPU time is more than the
transfer time it saves.

## Behind a reverse proxy

You don't need to change anything. nginx, Caddy and Traefik pass a response
that is already gzipped through unchanged.

To let the proxy compress instead, for example to use Brotli or zstd, set
`ResponseCompression__Enabled=false` and enable compression at the proxy. With
nginx, include the MemoryPack type: `gzip_proxied any;` and
`gzip_types application/json application/x-memorypack;`.

## Cross-site cookies

Compressing HTTPS responses is safe with Flare's default `SameSite=Lax` session
cookie. A third-party page can't make a signed-in browser send the repeated,
attacker-shaped requests that a compression side-channel attack (BREACH) needs.

If you set `Auth__CookieSameSite=None` (dashboard and API on different domains)
and your threat model includes someone watching the network between users and
Flare, set `ResponseCompression__Enabled=false`.

## Check that it works

```bash
curl -s -o /dev/null -D - -H 'Accept-Encoding: gzip' \
  -H "Authorization: Bearer $FLARE_PAT" http://localhost:8080/api/version | grep -i content-encoding
```

The output should be `Content-Encoding: gzip`. If it is empty, check
`ResponseCompression__Enabled`, and check whether a proxy in between strips
the request's `Accept-Encoding` header.
