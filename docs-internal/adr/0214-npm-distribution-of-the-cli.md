# ADR-0214: Distribute the CLI on npm as self-contained per-platform binaries

Status: Accepted

Date: 2026-10-13

## Context

`flare` (`src/Flare.Cli/`) is only installable as a
.NET global tool, which needs the .NET SDK. People who run a standing Flare stack are not all .NET developers, and the
CLI's only real prerequisite is Docker. Wrapping a self-contained .NET executable in an npm package is a known pattern
(a minimal example: `adrianstevens/npm-cli-wrapper`, which has no license, so nothing was copied). That example has gaps we
would inherit: x64 only, every platform's binary in one package, a Windows-only build script not wired to the package,
lost exit signals, and an install-time `chmod`.

## Decision

- Publish `@apardev/flare-cli` (a ~3 kB Node launcher) plus one binary package per platform,
  `@apardev/flare-cli-{darwin-arm64,darwin-x64,linux-arm64,linux-x64,win32-x64}`, declared as `optionalDependencies` with
  `os`/`cpu` fields and pinned to the exact same version, so npm downloads only the matching ~34 MB binary.
- The version is always `Flare.Cli.csproj`'s `<Version>`; `.github/workflows/npm-publish.yml` runs on the same
  `flare-cli-v*` tag as the NuGet publish and fails if the tag and csproj disagree.
- Binaries are `dotnet publish` single-file, self-contained, **not trimmed** (Spectre.Console.Cli and the MCP SDK use
  reflection). macOS targets are built on macOS runners so arm64 binaries get the ad-hoc signature macOS requires.
- The launcher forwards SIGTERM/SIGHUP, re-raises the child's terminating signal, and propagates its exit code. It does no
  install-time scripting (works with `--ignore-scripts`); platform packages are tarred between CI jobs to keep the exec bit.
- Publishing uses npm trusted publishing (OIDC) with provenance; no `NPM_TOKEN`.

## Consequences

- A third distribution of the CLI to keep at the same version as the NuGet package; the shared tag does that.
- Unsupported: musl Linux (Alpine) and Windows on ARM. They fall back to `dotnet tool install`; the launcher
  says so. Add RIDs in `scripts/build.mjs`, `launcher/flare.js` and the workflow matrix.
- `--omit=optional` installs break the CLI; the launcher's error message and the docs say so.
- Each of the six packages needs one manual first publish before a trusted publisher can be attached to it.
- A Node process running under Rosetta on Apple Silicon reports `x64` and gets the x64 binary, which runs there too.
