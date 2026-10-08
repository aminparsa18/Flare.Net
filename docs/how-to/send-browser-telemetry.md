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

If [ingest API keys](configure-authentication.md#ingest-api-keys) are required,
the exporter needs `headers: { Authorization: 'Bearer <key>' }`. That key ships
in your JavaScript, so every visitor can read it. Create a dedicated key for
the browser and restrict it to your site's origins:

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
outside a browser. Set [per-key limits](configure-authentication.md#ingest-api-keys)
to cap what a leaked key can send.

## Check it worked

Open the **Traces** page and filter by service `my-web-app`. A page load shows
as a `documentLoad` trace with `documentFetch` and `resourceFetch` child spans.
Calls to an API that propagates `traceparent` appear as one trace across both
services.

## Limits

- Only traces are covered here. Web vitals and JavaScript errors need extra
  instrumentation, and stack traces from minified bundles are not
  symbolicated yet.
- Browsers may drop the last batch when a tab closes.
