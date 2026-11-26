# ADR-0281: The Aspire hosting integration moves to the Community Toolkit

Status: Accepted

Date: 2026-11-27

## Context

Flare's Aspire hosting integration was donated to the Aspire Community Toolkit
([CommunityToolkit/Aspire#2370](https://github.com/CommunityToolkit/Aspire/pull/2370), merged
2026-11-10) and is published as `CommunityToolkit.Aspire.Hosting.Flare` 17.0.0. It depends on
`Aspire.Hosting` 17.0.0, and this repo moved to Aspire 17 in the same change set
(`chore: upgrade to Aspire 17`).

This repo still published two older packages:

- **`Flare.Hosting.Aspire`** (`src/Aspire.Hosting.Flare`, last release 0.5.1, built on Aspire
  13.x). `AddFlare("flare")` creates ClickHouse and Redis itself and offers `WaitForFlare`,
  `WithOtlpEndpoint`, per-image/per-port overrides, `WithColdStorage`, `WithPersistentStorage`
  (Kubernetes volumes) and `enableResourceGraph` (the Resources page).
- **`Flare.Aspire`** (`src/Aspire.Flare`, 0.2.0). Client-side `AddFlareOtlpExporter(name)`,
  reading the connection string `WithReference(flare)` injects. It overlaps with
  `Flare.OpenTelemetry`'s `AddFlareTelemetry` (ADR-0270), which reads a `Flare` config section
  instead.

The toolkit package has a different API: the caller passes ClickHouse and Redis in, the
returned resource is the ingest container (so `.WaitFor(flare)` works), and `WithOtlpExporter`
sets both endpoint and protocol and handles the API keys. It deliberately left out cold storage,
Kubernetes/Compose publish customization, the Resources page and per-container image/port
overrides. Its `FlareResource` exposes the same OTLP/gRPC connection string as the old one.

`examples/ExampleApp.AppHost` uses `Flare.Hosting.Aspire`, including `WithPersistentStorage`
for its k3s deploy. `src/Flare.AppHost` uses neither package.

## Decision

- **A. `Flare.Hosting.Aspire` is deprecated.** It is marked deprecated on nuget.org (reason
  Legacy, alternative `CommunityToolkit.Aspire.Hosting.Flare`, message naming the missing
  features). 0.5.1 is the last release and stays listed for Aspire 13.x users. Its tag trigger
  is removed from `nuget-publish.yml` and the project is `IsPackable=false`. The source stays,
  only because the example AppHost needs `WithPersistentStorage`. Releasing it again would
  mean an Aspire 17 build that duplicates the toolkit package.
- **B. `Flare.Aspire` is kept, not deprecated yet.** It works unchanged with the toolkit
  package's `WithReference(flare)`, and nothing else reads `ConnectionStrings:flare`. The docs
  stop presenting it as the main route. Once `Flare.OpenTelemetry` falls back to that
  connection string, `Flare.Aspire` gets deprecated in its favor.
- **C. The docs lead with the toolkit package.** `run-with-aspire`, the `aspire-hosting`
  reference and the Aspire tutorial use `CommunityToolkit.Aspire.Hosting.Flare` with
  `WithOtlpExporter`, state the Aspire 17 requirement, and carry an old-to-new migration table.
  The deprecated package's API and deployment facts move to a section of the reference page.
  CLAUDE.md's "three install paths" names the toolkit package.

## Consequences

- New work on the hosting integration happens in CommunityToolkit/Aspire, through their review,
  not here. The missing features are roadmap items to port there.
- With `WithOtlpExporter`, a service's telemetry goes to Flare instead of the Aspire dashboard.
  The docs say so and give `WithReference` + `Flare.Aspire`, or a Collector, for keeping both.
- Until the example AppHost can switch, the repo keeps compiling `Flare.Hosting.Aspire` against
  Aspire 17, so its API surface must keep building even though nothing ships from it.
- Users following old blog posts or the 0.5.1 README get a nuget.org deprecation warning that
  points at the toolkit package.
