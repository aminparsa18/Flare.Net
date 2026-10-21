# How to send browser telemetry to Flare

Send page loads, `fetch` spans and JavaScript errors from a web app to Flare with
the OpenTelemetry JavaScript SDK. Browser spans share a trace ID with your .NET
backend, so a slow click can be followed into the API call and its database
queries.

Flare's OTLP/HTTP receiver accepts the SDK's JSON exports directly. The only
extra step is telling it which origins may post to it.

## Prerequisites

- A running Flare instance with the OTLP/HTTP port (`4318`) reachable from your
  users' browsers.
- A web app you can add npm packages to.

## Allow your site's origin

Browsers refuse cross-origin requests unless the receiver answers a CORS
preflight. Flare sends no CORS headers by default. List your site's origin on
Flare.Ingest:

```bash
Otlp__AllowedOrigins__0=https://app.example.com
Otlp__AllowedOrigins__1=http://localhost:5173
```

Restart Flare.Ingest. Only `POST` is allowed, with the `Content-Type`,
`Content-Encoding` and `Authorization` headers. `*` allows any origin.

## Install and configure the SDK

```bash
npm install @opentelemetry/api @opentelemetry/sdk-trace-web \
  @opentelemetry/exporter-trace-otlp-http @opentelemetry/resources \
  @opentelemetry/instrumentation-document-load \
  @opentelemetry/instrumentation-fetch
```

```ts
import { WebTracerProvider, BatchSpanProcessor } from '@opentelemetry/sdk-trace-web';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { DocumentLoadInstrumentation } from '@opentelemetry/instrumentation-document-load';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';

const provider = new WebTracerProvider({
  resource: resourceFromAttributes({ 'service.name': 'my-web-app' }),
  spanProcessors: [
    new BatchSpanProcessor(
      new OTLPTraceExporter({ url: 'https://flare.example.com:4318/v1/traces' })
    )
  ]
});
provider.register();

registerInstrumentations({
  instrumentations: [
    new DocumentLoadInstrumentation(),
    new FetchInstrumentation({
      // Send the traceparent header to your own API so the trace continues server side.
      propagateTraceHeaderCorsUrls: [/api\.example\.com/]
    })
  ]
});
```

Your API must also allow the `traceparent` header in its own CORS policy, or the
browser blocks the call.

## Ingest keys

If [ingest API keys](../admin/configure-authentication.md#ingest-api-keys) are required,
the exporter needs `headers: { Authorization: 'Bearer <key>' }`. That key ships
in your JavaScript, so every visitor can read it. Create a dedicated key for
the browser and restrict it to your site's origins:

In the dashboard, open **Settings > Ingest keys** and use the globe button on a key to edit its allowed origins and services; `flare apikey scope` does the same from a terminal. The API calls behind them:

```bash
curl -X PUT http://localhost:8080/api/ingest-keys/$KEY_ID/origins \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "origins": ["https://app.example.com"] }'
```

A restricted key is accepted only from a browser request whose `Origin` matches
the list; curl and server exporters get `403`. A listed origin is also allowed
through CORS, so with a restricted key you don't need `Otlp__AllowedOrigins`.
Changes apply within 30 seconds. An empty list removes the restriction.

You can also pin the key to the services it may write for. An export containing any
other (or no) `service.name` is refused with `403`:

```bash
curl -X PUT http://localhost:8080/api/ingest-keys/$KEY_ID/services \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "services": ["my-web-app"] }'
```

This does not make the key secret: anyone can still forge an `Origin` header
outside a browser. Set [per-key limits](../admin/configure-authentication.md#ingest-api-keys)
to cap what a leaked key can send.

## Report JavaScript errors

Flare's **Errors** page groups exceptions recorded on spans. Report each uncaught error as a short span with an exception event:

```ts
import { trace, SpanStatusCode } from '@opentelemetry/api';

const tracer = trace.getTracer('browser-errors');

function report(error: unknown) {
  const err = error instanceof Error ? error : new Error(String(error));
  const span = tracer.startSpan('js.error');
  span.recordException(err);
  span.setStatus({ code: SpanStatusCode.ERROR, message: err.message });
  span.end();
}

window.addEventListener('error', (e) => report(e.error ?? e.message));
window.addEventListener('unhandledrejection', (e) => report(e.reason));
```

The errors appear on **Errors** grouped by type and message. Add the resource filter `telemetry.sdk.language = webjs` to see browser errors only. Throw `Error` objects: a thrown string has no type, so it is not grouped. Upload source maps to read minified stacks (see below).

## Report web vitals

Record each Core Web Vital as a histogram named `browser.web_vital.<name>` (`lcp`, `inp`, `cls`, `fcp`, `ttfb`) with the `rating` (`good`, `needs-improvement`, `poor`) as an attribute:

```bash
npm install web-vitals @opentelemetry/sdk-metrics \
  @opentelemetry/exporter-metrics-otlp-http
```

```ts
import { metrics } from '@opentelemetry/api';
import { MeterProvider, PeriodicExportingMetricReader } from '@opentelemetry/sdk-metrics';
import { OTLPMetricExporter } from '@opentelemetry/exporter-metrics-otlp-http';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { onCLS, onFCP, onINP, onLCP, onTTFB } from 'web-vitals';

const meterProvider = new MeterProvider({
  resource: resourceFromAttributes({ 'service.name': 'my-web-app' }),
  readers: [
    new PeriodicExportingMetricReader({
      exporter: new OTLPMetricExporter({ url: 'https://flare.example.com:4318/v1/metrics' }),
      exportIntervalMillis: 30_000
    })
  ]
});
metrics.setGlobalMeterProvider(meterProvider);
const meter = metrics.getMeter('web-vitals');

const MS = [100, 200, 500, 1000, 2000, 2500, 4000, 6000, 10000];
const SCORE = [0.01, 0.05, 0.1, 0.15, 0.25, 0.5, 1];

function track(name: string, unit: string, buckets: number[], subscribe: typeof onLCP) {
  const histogram = meter.createHistogram(`browser.web_vital.${name}`, {
    unit,
    advice: { explicitBucketBoundaries: buckets }
  });
  subscribe(({ value, rating }) => histogram.record(value, { rating }));
}

track('lcp', 'ms', MS, onLCP);
track('inp', 'ms', MS, onINP);
track('fcp', 'ms', MS, onFCP);
track('ttfb', 'ms', MS, onTTFB);
track('cls', '1', SCORE, onCLS);

// INP and CLS settle when the page is hidden; flush before the tab goes away.
document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'hidden') void meterProvider.forceFlush();
});
```

On the **Dashboards** page choose **Create from template > Web vitals**. The panels chart each vital's percentiles; Google's targets are measured at p75 (LCP under 2.5 s, INP under 200 ms, CLS under 0.1).

## Break vitals down by route

Add `url.template` (the route pattern such as `/orders/:id`, not the raw URL) to the attributes passed to `histogram.record`. Expanding a service on the **Frontend** page then shows each route's vitals. The **Page loads** column counts FCP measurements, one per page load.

## Symbolicate minified stack traces

Production bundles are minified, so an error's stack shows `at o (…/app-abc123.js:1:30)`. Upload the build's source maps and Flare shows the original function, file, line and column on **Errors** instead; an occurrence whose trace was rewritten carries a **Source map applied** badge.

First make the app report the release it was built from, using the same value you upload with:

```ts
resourceFromAttributes({ 'service.name': 'my-web-app', 'service.version': '1.4.2' })
```

Then upload the maps from your build output as a CI step, with a [personal access token](../admin/configure-authentication.md) of an admin in `FLARE_API_TOKEN`:

```bash
flare sourcemaps upload ./dist --url https://flare.example.com \
  --service my-web-app --release 1.4.2
```

Each `dist/**/*.map` file is stored under its path with `.map` removed, so `dist/assets/app-abc123.js.map` is stored as `assets/app-abc123.js`. A frame matches the longest stored path that its script URL ends with. `flare sourcemaps list` (or **Settings > Source maps**) shows what is stored and `flare sourcemaps delete --service my-web-app --release 1.4.2` removes a release. The raw API is `PUT /api/source-maps?service=&version=&bundle=` with the map JSON as the body.

Symbolication happens when you open an occurrence, so maps uploaded after an error still apply to it. A map is matched on `service.name` plus `service.version` (or the `vcs.revision` resource attribute), so keep old releases' maps while their errors still occur. Maps up to 50 MB are accepted; index maps (`sections`) are rejected. Do not serve the `.map` files publicly if you don't want your source readable, and keep them out of the deployed bundle.

## See it on the Frontend page

Open **Frontend** (in the user menu's **More** list). It shows each service's 75th-percentile LCP, INP, CLS, FCP and TTFB over the chosen window, coloured against Google's thresholds, with the share of measurements the browser rated good, and the top browser JavaScript errors. The errors are the exceptions from resources with `telemetry.sdk.language = webjs`; **Open in Errors** continues there. **Settings > Source maps** lists the uploaded maps by release and deletes them.

## Record session replay

Flare can replay what a user saw in the browser: page loads, clicks, scrolling and the text on the page, played back next
to the session's spans and errors. The recorder is `@apardev/flare-browser-replay`, a thin wrapper around
[rrweb](https://github.com/rrweb-io/rrweb) that sends the recording to Flare.Ingest's `POST /v1/replay`. It is optional
and records nothing until you start it.

```bash
npm install @apardev/flare-browser-replay
```

```ts
import { startReplay } from '@apardev/flare-browser-replay';

const replay = startReplay({
  endpoint: 'https://flare.example.com:4318',
  service: 'my-web-app',
  ingestKey: '<a browser-restricted ingest key>',
  sampleRate: 0.25 // optional: record one session in four
});
```

The origin must be allowed in the same way as for OTLP (see [Allow your site's origin](#allow-your-sites-origin)); the
same ingest key works, and a key restricted to services must list `my-web-app`. An existing ClickHouse needs
[migration 0090](../../../db/clickhouse/0090_web_replay_chunks.sql).

### Link the recording to the session's spans

A recording is stored under a session id, and Flare shows it on the session page for that id. Spans appear on the same
page when they carry it as the `session.id` attribute. `replay.sessionId` is the id for this tab, so copy it onto your
spans:

```ts
import type { Context } from '@opentelemetry/api';
import type { ReadableSpan, Span, SpanProcessor } from '@opentelemetry/sdk-trace-web';

class SessionIdProcessor implements SpanProcessor {
  onStart(span: Span, _context: Context) { span.setAttribute('session.id', replay.sessionId); }
  onEnd(_span: ReadableSpan) {}
  forceFlush() { return Promise.resolve(); }
  shutdown() { return Promise.resolve(); }
}
// add it to the WebTracerProvider's spanProcessors, before the BatchSpanProcessor
```

Then open **Sessions**, pick the session and press **Watch recording**. **Show in replay** on a span row jumps the player
to that span's start. A session that has a recording but no spans is still reachable at `/sessions/<id>`.

### What is masked

Everything typed into a form field is replaced with asterisks, and a password field always is. Beyond that:

| To | Do |
| --- | --- |
| Leave an element out entirely (it replays as an empty box) | add the attribute `data-flare-block` or the class `flare-block` |
| Hide an element's text | add `data-flare-mask` or the class `flare-mask` |
| Hide all text on the page | `startReplay({ ..., maskAllText: true })` |
| Show form fields as typed | `startReplay({ ..., maskAllInputs: false })` (not recommended) |

Masking happens in the browser before anything is sent, so it is only as good as the markup: Flare cannot tell what a
recorded event contains, and anyone holding an ingest key can post any events. Review a few recordings before turning replay
on for all users, and mark anything personal that is not a form field (an account page, an address in rendered text).
Stylesheets are inlined into the recording, but images and fonts are loaded from your site when someone watches, so the
viewer's browser must be able to reach it.

### Options

| Option | Default | Meaning |
| --- | --- | --- |
| `sessionId` | one per tab, kept in `sessionStorage` | The id the recording is stored under. Set it to reuse an id you already have. |
| `sampleRate` | `1` | Share of sessions to record, decided once per session. |
| `flushIntervalMs` | `5000` | How often a batch is sent. |
| `maxEventsPerChunk` | `500` | A batch is sent early at this many events. |
| `maxDurationMs` | one hour | Recording stops after this long; the dashboard lists one hour. |
| `checkoutEveryMs` | off | Take a fresh page snapshot this often, so a replay can start later in a long session. |
| `mode` | `'session'` | `'session'` records everything; `'onError'` records only around errors (below). |
| `bufferMs` | `30000` | `onError` only: how far back an error's recording reaches. |
| `afterErrorMs` | `10000` | `onError` only: how long recording continues after an error. |
| `captureErrors` | `true` | `onError` only: treat uncaught errors and unhandled promise rejections as errors to keep. |
| `blockSelector`, `maskTextSelector` | `[data-flare-block]`, `[data-flare-mask]` | Selectors for the two attributes above. |
| `onStop` | none | Called with a reason when the recorder stops by itself. |

`replay.stop()` ends the recording and sends what is left; `replay.flush()` sends what has been recorded so far.

### Record only around an error

Recording every session is the most to store and the most to review. With `mode: 'onError'` the recorder keeps only the last
seconds in memory and sends nothing until something goes wrong:

```ts
const replay = startReplay({ endpoint, service: 'my-web-app', ingestKey, mode: 'onError', bufferMs: 30_000, afterErrorMs: 10_000 });

try { await checkout(); } catch (err) { report(err); replay.keep(); }   // an error you handled yourself
```

An uncaught error or an unhandled promise rejection does the same without any code (turn it off with `captureErrors: false`).
On an error the recorder sends what it holds (between `bufferMs` and twice `bufferMs` before the error, starting at a page
snapshot) and keeps recording for `afterErrorMs`. Then it drops events until the next snapshot, so a session with several
errors becomes several recordings, and a session without one leaves nothing behind. Nothing leaves the browser until the
error, but the page is still recorded into memory the whole time, so the masking rules above apply either way.

### Find a recording

On **Sessions**, a session with a recording shows a play icon next to its id, and **Recorded only** hides the rest. On
**Errors**, an occurrence whose span carries the `session.id` attribute has a **View session replay** link that opens the
session at the moment of the error, a few seconds early.

### What to expect

- A batch is sent every few seconds and when the tab is hidden. A failed send is dropped, not retried, so a replay can have a
  gap but never plays an event twice. Closing a tab can lose the last batch of more than about 60 KB.
- A recording stops by itself after an hour or when the key is rejected (`401` or `403`), and `onStop` is called. When the key
  is over a limit (`429`), the recorder drops batches for the time the response's `Retry-After` gives and carries on after it.
- Recordings count against the Traces retention, and a long page with a lot of DOM changes is large. Use `sampleRate` on busy
  sites, or `mode: 'onError'`. A recording counts against the key's byte limits (the **Bytes** caps per minute and per day under the
  key's **Limits**, the same ones OTLP uses) but not its event limits, since a replay event is not a telemetry record.
- Over 40 MB of stored recording, the player warns before loading it into the tab.

## Check it worked

Open the **Traces** page and filter by service `my-web-app`. A page load shows
as a `documentLoad` trace with `documentFetch` and `resourceFetch` child spans.
Calls to an API that propagates `traceparent` appear as one trace across both
services.

## Limits

- Browsers may drop the last batch when a tab closes.
