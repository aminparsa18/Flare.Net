# How to track releases and the errors they introduce

Marking a release tells Flare that a version of a service went out, with its commit and deploy time. The **Releases** page then lists, per version, the exception groups that first appeared in it.

## Mark a release from your pipeline

Call the API from the deploy step with a personal access token (**Settings > Access tokens**) of a Member or Admin:

```bash
curl -X PUT "$FLARE_URL/api/releases" \
  -H "Authorization: Bearer $FLARE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"service":"orders-api","version":"2.4.0","commit":"'"$GIT_SHA"'","url":"'"$RUN_URL"'"}'
```

| Field | Meaning |
| --- | --- |
| `service` | The service's `service.name`. Required |
| `version` | Exactly the `service.version` the service reports. Required |
| `commit`, `url`, `notes` | Optional. `url` must be an http(s) link to the commit, pull request or pipeline run |
| `deployedAt` | When it went out (ISO 8601). Defaults to now |

Marking the same service and version again updates the marker. On a local stack, `flare releases mark orders-api 2.4.0 --commit $SHA` does the same, `flare releases list --service orders-api` shows the result and `flare releases delete` removes a marker. Removing a marker never touches telemetry.

## Read the Releases page

Open **Releases** from the menu and pick a service. Each row is a marked version with its deploy time, commit and **New errors**: the number of exception groups whose earliest recorded occurrence was under that version. Expand a row to see those groups, most frequent first, each linking to the Errors page scoped to the service and version.

A group counts as new when it had no occurrence in the 30 days before the deploy. A group that went quiet for longer than that and then came back reads as new in the version where it returned.

## Regressions

A group you resolved on the [Errors page](triage-errors.md) is shown as **Regressed** when it recurs in a version it had not been seen in. That works from `service.version` alone, with or without release markers. The Releases page answers the other questions: which errors a given version added, and which resolved groups came back in it. The **Regressions** column counts the latter, and expanding a row lists those groups first, with when each was resolved. The match is on the version string alone, so two services that share a version number share its regressions.

## Deploy markers on charts

The Logs volume chart and the Metrics chart draw a dashed vertical line at each release deployed inside the chart's time range, for the services the chart is filtered to (every service when none is). Hover a line to see the service, version and time. At most the 12 newest are drawn. Dashboard panels do not draw them.

## Alert on a bad release

Create an alert rule with the condition **New errors after a release**. Pick the services to watch (all services when none is chosen), a threshold such as `>= 1`, and a window such as 3600 seconds. The rule looks at releases marked in the last window and fires when the worst of them introduced at least that many new exception groups, so it stays quiet between deploys. The notification links to the Releases page. It has no no-data option, since no new errors is the healthy state.

The same rule over the API is `conditionKind: "ReleaseErrors"` with `exceptionCondition: { "exceptionType": "", "filter": { "services": ["orders-api"] } }`; only the filter's services are read.

## Limits

- Only exception events on spans are counted, grouped by exact type and message, like the Errors page.
- Telemetry without `service.version` is not attributed to any release.
- The new-error counts are computed from your spans when the page loads, for the selected service only.
