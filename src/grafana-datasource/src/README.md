# Flare data source

Query [Flare](https://github.com/aminparsa18/Flare.Net) from Grafana for what the Prometheus, Loki and Tempo compatible APIs
cannot express:

- **Sessions** and **Session timeline**: client app sessions in the time range, one row each, and one session's spans in order.
- **Errors** and **Error issues**: exception groups ranked by occurrences, and their triage state.
- **Release health** and **trend**: crash-free sessions and users per app version, as a table or a time series.
- **App starts** and **Screen performance**: cold and warm start durations, screen load time and slow or frozen frames.
- **Releases**: the deploys you marked, also usable as dashboard annotations.

A **Flare app health** dashboard is bundled: import it from the data source's Dashboards tab.

Template variables list services and versions. The plugin backend calls Flare.Api from the Grafana server, so that server needs
network access to Flare.Api; the personal access token is stored encrypted by Grafana and never reaches the browser.

Alert rules, public dashboards and reports work with these queries. For an alert rule use **Release health trend**, the one
query that returns a time series (add a Reduce and a Threshold expression); the table queries cannot be alert conditions.
