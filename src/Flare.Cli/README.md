# Flare.Cli

> Package ID `Flare.Cli`, not `flare` - that id is already taken on nuget.org (an old,
> unrelated, unlisted package). The installed command is still `flare`:
> `dotnet tool install --global Flare.Cli`, then `flare start`.

A global .NET tool that manages a **standing, standalone** [Flare](https://github.com/aminparsa18/Flare.Net)
instance - the same non-Aspire Docker stack `docker compose up` brings up in the
Flare.Net repo itself, wrapped as `flare start`/`stop`/`status`/`open`/`update`/`logs`/
`doctor`/`destroy`, installable and runnable from anywhere, no repo checkout required.

This is **not** an Aspire integration - if your app already has an AppHost, use
[`Flare.Hosting.Aspire`](https://www.nuget.org/packages/Flare.Hosting.Aspire) instead;
`aspire start` already orchestrates that path. `flare` exists for the case Aspire mode
can't cover: one long-running Flare instance you point many unrelated local projects'
OTLP output at, independent of any single AppHost's lifecycle.

```
flare start   # first run also initializes ~/.flare/ with a generated compose file + .env
flare open    # launches the dashboard in your browser
flare stop    # pauses the stack - data volumes are kept
```

See [docs/reference/cli-commands.md](https://github.com/aminparsa18/Flare.Net/blob/main/docs/reference/cli-commands.md)
for the full command reference and `~/.flare/` state directory layout, and
[docs/how-to/run-with-cli.md](https://github.com/aminparsa18/Flare.Net/blob/main/docs/how-to/run-with-cli.md)
for known limitations.
