# ADR-0270: A one-line OpenTelemetry setup package for .NET (`Flare.OpenTelemetry`)

Status: Accepted

Date: 2026-10-21

## Context

Sending a .NET app's telemetry to Flare meant wiring the OpenTelemetry SDK by hand: an OTLP exporter per signal with the
endpoint, protocol and ingest-key header, the ASP.NET Core, `HttpClient` and runtime instrumentation, and optionally a
sampler. `Flare.Aspire` already does the exporter part, but it is shaped for Aspire: it reads a connection string that an
AppHost injects through `.WithReference(flare)`, it is named and described as an Aspire integration, and it sets neither
instrumentation nor a sampler. An app that does not run under Aspire had no short path. The roadmap item asked for a thin
helper, not a proprietary format, so that it stays inside [ADR-0012](0012-otlp-only-ingestion.md) (OTLP only).

## Decision

- **A new package, `Flare.OpenTelemetry`, not an extension of `Flare.Aspire`.** The name is what a non-Aspire developer
  searches for. `Flare.Aspire` keeps its job (read the injected connection string) and is unchanged. The two do not depend
  on each other and both use named exporters, so an Aspire app that also wants the instrumentation defaults can call both
  without the exporters colliding. The cost is a second PackageId, a `flare-opentelemetry-v*` tag in
  `nuget-publish.yml`, and a nuget.org trusted-publishing policy for it.
- **`builder.AddFlareTelemetry(endpoint, apiKey)` or `AddFlareTelemetry(o => ...)`, on `IHostApplicationBuilder`.** Named
  `AddFlareTelemetry`, not `AddFlare`, because `AddFlare` is already `Flare.Hosting.Aspire`'s AppHost method and two
  same-named methods on different builders in docs and autocomplete would be confusing.
- **Options bind from the `Flare` configuration section, then the delegate overrides.** A deployment sets
  `Flare__Endpoint`, `Flare__ApiKey` and `Flare__TraceSampleRatio` as environment variables and the code stays one call.
  With nothing set it exports to `http://localhost:4317`, Flare.Ingest's conventional gRPC port.
- **Bundled defaults match `Flare.ServiceDefaults`:** ASP.NET Core and `HttpClient` instrumentation for traces and
  metrics, and runtime metrics, each switchable off. They add three OpenTelemetry instrumentation packages as dependencies
  of every consumer, which is the price of the "one line"; the dependency-free alternative is `Flare.Aspire` or hand wiring.
- **The sampler is parent-based, and only installed below a ratio of 1.** At the default `TraceSampleRatio = 1` the SDK's
  own default stays in place. Below 1, `ParentBasedSampler(TraceIdRatioBasedSampler)` makes the root service decide and
  every joining service follow, so a kept trace stays whole. It is head sampling in the app and independent of ingest
  sampling ([ADR-0122](0122-ingest-trace-sampling.md)).
- **One named exporter per signal (`flare-logs`, `flare-traces`, `flare-metrics`).** Named exporters are additive next to an
  unnamed one the app already has. A single shared name looked equivalent and was wrong: the three signals then share one
  `OtlpExporterOptions` instance and the last configure call wins, so logs and traces were exported to the metrics path.
  Found by running the SDK against a local listener, and covered by a test that resolves each signal's options.
- **For OTLP/HTTP the signal path is added by the helper.** An endpoint set in code is used verbatim by the SDK, so
  `/v1/traces`, `/v1/logs` and `/v1/metrics` are appended for `HttpProtobuf`; for gRPC the endpoint is used as given.
- **No Aspire dependency.** The package references only `Microsoft.Extensions.*` abstractions and the OpenTelemetry
  packages.

## Consequences

- A .NET app is one `dotnet add package` and one call from sending all three signals, with or without Aspire.
- `Flare.Aspire` and `Flare.OpenTelemetry` overlap on the exporter; they are small enough that sharing code between them
  is not worth a third package.
- The package is pre-alpha like the others (0.1.0) and not yet on nuget.org; releasing it needs the trusted-publishing
  policy and a `flare-opentelemetry-v0.1.0` tag.
