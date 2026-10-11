# Tutorial: your first alert

By the end of this tutorial you'll have an alert rule that watches your logs,
you'll have made it fire on purpose, seen the notification arrive, and
acknowledged it. It takes about ten minutes.

This is the third tutorial. It assumes Flare is running from
[Tutorial: your first Flare logs](getting-started.md) and that you still have
the `flare-hello` app from it. You don't need the app from
[Tutorial: your first trace](first-trace.md).

## Prerequisites

- Flare running (the standalone `docker compose up` stack)
- The `flare-hello` console app from the first tutorial
- A place to receive a webhook. The quickest is a throwaway URL from
  [webhook.site](https://webhook.site): open it and copy the unique URL it
  gives you. For a real Slack channel, use an incoming-webhook URL instead; the
  steps are identical.

## 1. Make `flare-hello` log errors

An alert needs something to count. Change the last lines of `flare-hello`'s
`Program.cs`, replacing the single `LogInformation` call, so it logs a few
errors:

```csharp
for (var i = 0; i < 5; i++)
{
    logger.LogError("payment failed for order {OrderId}", 1000 + i);
}
```

Don't run it yet. First tell Flare what to watch for.

## 2. Create the rule

Open [http://localhost:7777](http://localhost:7777), choose **Alerts**, and
click **New alert**. Fill in the form:

- **Name**: `Payment errors`
- **Condition**: **Log filter**
- **Service**: `flare-hello`
- **Level**: `Error`
- **Threshold**: 3 **events in** 300 seconds. The rule fires when at least 3
  matching log events land inside a rolling 300-second window.
- **Cooldown (seconds)**: leave the default. It's the minimum time between two
  notifications while the rule keeps breaching.
- **Notify via**: **Webhook / Slack**, with your webhook URL in **Webhook URL**.

Before saving, click **Test against current data**. It dry-runs the rule
against what's in Flare now, ignores cooldown and sends nothing. There are no
errors yet, so it reports a count of 0, below the threshold. Click **Create
alert**.

## 3. Check the channel works

A rule can be perfectly written and still never reach you if the webhook URL
is wrong, so verify delivery before you rely on it. In the rules table, use the
row's **Send a test alert through this rule's channel** action. A test
notification should appear at your webhook URL within a few seconds. This
doesn't count as a real firing and doesn't start an incident.

## 4. Make it fire

Now run the app:

```sh
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
export OTEL_SERVICE_NAME=flare-hello
dotnet run
```

It logs five errors and keeps running (press Ctrl+C when you're done). Flare
evaluates rules on a short poll interval, so within about a minute the rule's
status on the **Alerts** page changes to firing, and a notification arrives at
your webhook with the rule name, the observed count and a link back to the
rule.

Click **Test against current data** on the rule again to see the same number
the evaluator saw: 5 events, against a threshold of 3.

## 5. Acknowledge it

While the rule stays breached, it notifies again every cooldown period. When
you're already working on it, that's noise. In the rule's row, open the
acknowledge button (the double check mark), add a note such as `looking at
it`, and choose **Acknowledge**. The row now shows who acknowledged it, and no
further "fired" notifications are sent for this incident.

The same thing works from a terminal:

```sh
flare alerts list
flare alerts ack <rule-id> --note "looking at it"
```

## 6. Let it resolve

Once five minutes pass without three new errors, the rule's condition clears
and it resolves. The acknowledgement ends with the incident, and a **resolved**
notification is sent so everyone who heard about the problem hears it's over.
If errors return later, that's a new incident and it will notify again.

## What you've learned

- That an alert rule is a saved log filter, a count threshold over a rolling
  window, and a notification channel.
- How to check a rule against live data, and a channel against a real
  endpoint, before relying on either.
- How an incident moves from firing to acknowledged to resolved, and why an
  acknowledgement stops the repeat notifications but not the resolved one.

## Next steps

- Quieting a noisy alert for a fixed time instead:
  [`../how-to/alerting/acknowledge-and-snooze-alerts.md`](../how-to/alerting/acknowledge-and-snooze-alerts.md).
- Alerting on something other than log counts (metric thresholds, exception
  counts, anomalies, missing data): the **Condition** dropdown in the same
  form. Alerts on service-level objectives are covered in
  [`../how-to/alerting/define-slos.md`](../how-to/alerting/define-slos.md).
- Sharing one message layout across rules:
  [`../how-to/alerting/share-alert-notification-templates.md`](../how-to/alerting/share-alert-notification-templates.md).
- Keeping alert rules in git: `flare alerts export` and `flare config apply`,
  documented in [`../reference/cli-commands.md`](../reference/cli-commands.md).
