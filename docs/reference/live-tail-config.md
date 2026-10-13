# Live-tail configuration keys

Exact configuration keys for the live-tail WebSocket endpoints (`GET /api/logs/tail`, host stats, resource graph), set on `Flare.Api`.

| Key | Env var | Type | Default | Meaning |
|---|---|---|---|---|
| `LiveTail:PollDelay` | `LiveTail__PollDelay` | timespan | `250ms` | Wait between Redis stream reads when nothing new arrived. |
| `LiveTail:ReadBatchSize` | `LiveTail__ReadBatchSize` | int | `1000` | Maximum entries read per stream read. |
| `LiveTail:SubscriberChannelCapacity` | `LiveTail__SubscriberChannelCapacity` | int | `500` | Per-connection buffer. A client that cannot keep up loses events past this point instead of slowing everyone else. |
| `LiveTail:MaxClientMessageBytes` | `LiveTail__MaxClientMessageBytes` | int | `65536` | Largest message a client may send (summed across fragments). Larger messages close the socket with code 1009. |
| `LiveTail:MaxConnectionsPerUser` | `LiveTail__MaxConnectionsPerUser` | int | `10` | Concurrent live-tail sockets per user. Anonymous callers share one bucket. Past the cap the upgrade is refused with 429. |
| `LiveTail:MaxConnections` | `LiveTail__MaxConnections` | int | `200` | Concurrent live-tail sockets across all users. Past the cap the upgrade is refused with 429. |
| `LiveTail:MaxSocketLifetime` | `LiveTail__MaxSocketLifetime` | timespan | `00:05:00` | Longest a socket stays open before the server closes it and the dashboard reconnects. Zero or negative disables the cap. See [Socket lifetime](#socket-lifetime). |
| `LiveTail:MaxFilterItems` | `LiveTail__MaxFilterItems` | int | `100` | Maximum entries in one filter: services, severities, scope names, attribute and body-JSON filters, and their `in`/`not-in` values combined. |
| `LiveTail:MaxFilterStringLength` | `LiveTail__MaxFilterStringLength` | int | `1024` | Maximum length of any single string in a filter (search text, values, keys, paths). |
| `LiveTail:MaxRegexPatterns` | `LiveTail__MaxRegexPatterns` | int | `10` | Maximum `regex`/`not-regex` operators in one filter. |
| `LiveTail:MaxRegexPatternLength` | `LiveTail__MaxRegexPatternLength` | int | `256` | Maximum length of one regex pattern. |
| `LiveTail:RegexMatchTimeout` | `LiveTail__RegexMatchTimeout` | timespan | `50ms` | Per-match timeout. A match that exceeds it counts as no match. |

## Origin check

Browsers do not apply CORS to WebSockets, so `Flare.Api` rejects an upgrade whose `Origin` header is not listed in `Cors:AllowedOrigins` (env var `Cors__AllowedOrigins__0`, `__1`, ...) with 403. Requests without an `Origin` header, such as the `flare` CLI, are allowed. If the dashboard is served from an origin that is not in this list, live tail will not connect.

## Socket lifetime

Authorization is checked only when the socket opens. `LiveTail__MaxSocketLifetime` closes every live-tail, host-stats and resource-graph socket after that long and the dashboard reconnects, so a revoked session, a disabled user, an expired personal access token or a removed project membership stops streaming within that window.

## Regex patterns

Live-tail regexes run in linear time (`RegexOptions.NonBacktracking`). Lookarounds and backreferences are rejected when the filter is submitted, so a saved view that uses them stops working in live tail and needs rewriting. Search and aggregate queries are unaffected.

The defaults assume one live-tail socket per open dashboard tab, plus one host-stats and one resource-graph socket on the Resources page, so 10 per user covers a power user with several tabs. A stale socket from a closed tab frees its slot when the server notices the disconnect or at `MaxSocketLifetime`. Raise `MaxConnectionsPerUser` if users see 429 on reconnect, and `MaxConnections` if you have more than about 20 concurrent users. The filter limits are well above what the dashboard produces; raise them only for scripted clients. Host stats and the resource graph apply `MaxConnectionsPerUser`, `MaxConnections` and `MaxClientMessageBytes` too, each endpoint with its own bucket, so they cannot starve live tail. Their inbound frames are discarded, so only the socket count and the message size matter.

## Related

- [Authentication config](authentication-config.md)
