# Flare data source

Query [Flare](https://github.com/aminparsa18/Flare.Net) from Grafana for what the Prometheus, Loki and Tempo compatible APIs
cannot express:

- **Sessions**: client app sessions in the time range, one row each, with errors and screens.
- **Errors** and **Error issues**: exception groups ranked by occurrences, and their triage state.
- **Release health** and **trend**: crash-free sessions and users per app version, as a table or a time series.
- **Releases**: the deploys you marked, also usable as dashboard annotations.

Template variables list services and versions. The queries run in the browser and reach Flare through Grafana's data source
proxy, so the Grafana server needs network access to Flare.Api; the personal access token is stored encrypted by Grafana.

Alerting is not supported by this data source; use the `flare_release_*` metrics through a Prometheus data source for that.
