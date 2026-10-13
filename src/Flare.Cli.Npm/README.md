# @apardev/flare-cli

The [Flare](https://github.com/aminparsa18/Flare.Net) CLI, installable with npm. It manages a standing,
self-hosted Flare stack (`flare start`, `stop`, `status`, `update`, `open`, `logs`, `doctor`, `export`, `mcp`, …).
The binary is self-contained, so **no .NET SDK or runtime is needed**. Docker is.

```sh
npm install --global @apardev/flare-cli
flare start
```

Or run it once without installing: `npx @apardev/flare-cli doctor`.

Supported: macOS (arm64, x64), Linux (glibc, arm64, x64), Windows (x64). On anything else, use the .NET tool:
`dotnet tool install --global Flare.Cli`. Both routes install the same `flare` command; use one of them, not both.

See the [CLI guide](https://github.com/aminparsa18/Flare.Net/blob/main/docs/how-to/deploy/run-with-cli.md) and the
[command reference](https://github.com/aminparsa18/Flare.Net/blob/main/docs/reference/cli-commands.md).
Do not install with `--omit=optional` (or `--no-optional`): the binary lives in an optional per-platform package,
`@apardev/flare-cli-<os>-<cpu>`.
