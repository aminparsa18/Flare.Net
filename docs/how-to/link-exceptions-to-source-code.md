# How to link exception stack traces to your source code

On the **Exceptions** page, open a group to see its sample occurrences. Once a
service has a source repository configured, each stack frame's file location
becomes a link to that file and line in your repository, at the commit the
service was built from.

## Prerequisites

- Stack traces with file locations. .NET prints `in /src/File.cs:line 42` when
  the build ships portable PDBs, which is the default.
- You are an Admin, or authentication is off. Viewers and Members see the links
  but can't edit the configuration.

## Stamp the build's commit

Flare picks the commit from the span's resource attributes, in this order:

1. `vcs.ref.head.revision`
2. `vcs.revision`
3. `service.version`

For .NET, the simplest route is SourceLink, which puts the commit into the
informational version (`1.2.3+abc1234`). Flare reads the part after the `+`.
If `service.version` is plain `1.2.3`, it names no commit, so Flare falls back
to the default branch or tag you configure below.

## Configure the repository

1. In an occurrence's row, click the link icon beside **Show stack trace**.
2. Pick the host: GitHub, GitLab or Azure DevOps.
3. Enter the repository URL, for example `https://github.com/acme/shop`. For
   Azure DevOps use `https://dev.azure.com/org/project/_git/repo`.
4. Optionally set a fallback branch or tag, such as `main`.
5. Set the **path prefix** if your build path isn't repository-relative. It is
   the directory your build ran in, which Flare removes from each frame. For
   example `/src/` in a Docker build, or `/home/runner/work/shop/shop/` on
   GitHub Actions.
6. Click **Save**.

Builds with `<Deterministic>` and `ContinuousIntegrationBuild` already rewrite
paths to start with `/_/`, which Flare strips without a prefix.

## Show the failing lines inline

When a frame links to your repository, a **Show source** button appears under the
stack trace. It shows the lines around the throw site (the first linkable frame).

Flare's API fetches the file from your repository host, so a private repository
needs a read-only access token. Enter it in the same link-icon form:

- GitHub: a fine-grained token with read access to **Contents**.
- GitLab: a token with the `read_repository` scope.
- Azure DevOps: a personal access token with **Code (Read)**.

The token is write-only: Flare never shows it again, and leaving the field blank
keeps the saved one. Public repositories work without a token. Flare doesn't
follow redirects, ignores files over 2 MB and caches a file for 10 minutes.

## When a frame isn't linked

Flare leaves a frame as plain text rather than guess:

- the path is absolute and doesn't start with the configured prefix;
- the location is a bare file name with no directory, as Java frames are;
- the occurrence has no commit and the service has no fallback branch.

## See also

- [Architecture decision: ADR-0095](../../docs-internal/adr/0095-exception-source-links.md)
- [ADR-0096](../../docs-internal/adr/0096-inline-exception-source.md)
