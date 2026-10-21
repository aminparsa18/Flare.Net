# @apardev/flare-browser-replay

Browser session replay for [Flare](https://github.com/aminparsa18/Flare.Net). It records the page with
[rrweb](https://github.com/rrweb-io/rrweb) and sends the events to Flare.Ingest's `POST /v1/replay`; the Flare dashboard
plays them back on the session page, next to the session's spans and errors. Design and decisions:
[ADR-0264](../../docs-internal/adr/0264-browser-session-replay.md). The full guide, with masking, options and limits, is
[How to send browser telemetry to Flare](../../docs/how-to/instrument/send-browser-telemetry.md#record-session-replay).

```bash
npm install @apardev/flare-browser-replay
```

```ts
import { startReplay } from '@apardev/flare-browser-replay';

const replay = startReplay({
  endpoint: 'https://flare.example.com:4318', // Flare.Ingest's HTTP receiver
  service: 'my-web-app',
  ingestKey: '<a browser-restricted ingest key>'
});

replay.sessionId; // set this as the `session.id` attribute on your spans to see them next to the replay
```

Every form field is masked by default. Add `data-flare-block` to leave an element out and `data-flare-mask` to hide its
text; see the guide for the rest. Masking is done here, in the browser: the server stores what it is sent.

## Layout

| File | What it does |
| --- | --- |
| `src/recorder.ts` | `startReplay`: starts rrweb, flushes on a timer, at a size limit and when the tab is hidden, stops after `maxDurationMs` or when the key is rejected |
| `src/buffer.ts` | `ChunkBuffer`: collects events and hands them out as the JSON array text the server expects |
| `src/transport.ts` | `sendChunk`: gzip (when the browser has `CompressionStream`), `keepalive` for the last small batch, no retries |
| `src/session.ts` | The per-tab session id and the once-per-session sampling decision, both kept in `sessionStorage` when it is available |
| `src/options.ts` | Option defaults and the upload URL |

## Develop

```bash
npm install
npm run check   # tsc --noEmit
npm test        # node:test over the pure parts: buffer, options, session id, sampling, transport
npm run build   # dist/
```

The tests do not start a browser, so `startReplay` itself (rrweb wiring, the timers, the hidden-tab flush) has no automated
test. Check it by building a bundle, loading it in a page, and watching the chunks arrive at a running Flare.Ingest.
