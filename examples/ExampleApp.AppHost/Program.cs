var builder = DistributedApplication.CreateBuilder(args);

// Registers Docker Compose as a deployment target - inert for the default `aspire run`/
// `dotnet run` inner loop, but makes `aspire publish`/`aspire do prepare-compose`/`aspire
// deploy` produce a real docker-compose.yaml for this whole AppHost (Flare included). See
// docs/aspire-hosting.md's "Publishing / deploying via aspire publish" section - this is
// its worked example. AddFlare's publicApiUrl/publicDashboardUrl parameters below only
// matter once actually deploying off this machine; leave them unset for `aspire run`.
builder.AddDockerComposeEnvironment("env");

// enableResourceGraph defaults to false (see its doc comment on AddFlare) - left off here
// too, so this example's default footprint doesn't grow a Docker-socket-proxy sidecar for
// everyone who runs it. Pass `enableResourceGraph: true` to exercise the dashboard's
// Resources page against this AppHost - see docs/aspire-hosting.md.
//
// imageTag: "edge" is explicit and deliberate here, overriding AddFlare's own pinned-stable
// default - this example ProjectReferences Flare's local, unreleased source (see this
// project's .csproj), so it should always validate against main-tip images, not whatever
// stable version the published NuGet package currently pins.
var flare = builder.AddFlare("flare", imageTag: "edge");

// .WithReference(flare) injects ConnectionStrings__flare (Flare.Ingest's OTLP/gRPC endpoint) -
// ExampleApp.LogGenerator reads it via Flare.Aspire's builder.AddFlareOtlpExporter("flare")
// instead of the ambient OTEL_EXPORTER_OTLP_ENDPOINT env var WithOtlpEndpoint(flare) used to set.
//
// .WaitFor(flare) would NOT actually wait - flare is a lifetime-less grouping node and Aspire
// unconditionally skips those as WaitFor targets (see WaitForFlare's doc comment). WaitForFlare
// waits on the real dashboard resource instead, which transitively depends on everything else.
builder.AddProject<Projects.ExampleApp_LogGenerator>("log-generator")
    .WithReference(flare)
    .WaitForFlare(flare);

builder.Build().Run();
