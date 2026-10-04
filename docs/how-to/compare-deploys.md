# How to compare two deploys of a service

Flare compares two versions of a service so you can answer "did my deploy break anything?" on one screen. It needs no new instrumentation beyond a version on your service.

## Set the version

Set the `service.version` resource attribute. In .NET, with OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("orders-api", serviceVersion: "2.4.0"));
```

Any string works (a semantic version, a build number, a commit hash). Versions are read from your spans and logs, so a new deploy shows up as soon as it sends telemetry.

## Compare

1. Open **Traces**, then the **Services** tab.
2. Open a service (click its node in the **Map** view).
3. The **Deploy comparison** section is under the runtime health findings. By default it compares the newest version with the one first seen before it. Use the two selectors to pick another pair.

The comparison looks back 7 days. If the service sent only one version in that time, or none has a `service.version`, the section says so.

| Part | What it shows |
| --- | --- |
| Endpoints | Server and consumer spans by name: calls, error rate and p95 latency under each version. **Regressed** marks an error rate up by a percentage point or more, or a p95 up by a quarter and at least 20 ms. **New** and **Gone** mark endpoints only one version served |
| New exception types | Exception types seen under the current version and never under the baseline |
| New outbound dependencies | External hosts and database systems called under the current version and never under the baseline |
| New log patterns | Drain log patterns seen under the current version and never under the baseline |

Endpoints, exception types and the **Logs for this version** link open the matching explorer scoped to that service and version, over the window the current version was seen in.

## Limits

- "First seen" is measured inside the 7-day lookback, so a baseline that stopped sending before it began makes everything look new.
- Telemetry without `service.version` is not compared.
- Results are computed from your data when you open the section. The API behind it is `POST /api/services/version-comparison`.
