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

## Explain an exception with AI (optional)

Flare can ask a language model to explain an exception. It is off by default and
uses a model you bring: any OpenAI-compatible endpoint, including a local Ollama.
Set these on `Flare.Api`:

```bash
Ai__Enabled=true
Ai__Endpoint=http://localhost:11434/v1   # base URL; Flare calls /chat/completions
Ai__Model=llama3.1
Ai__ApiKey=...                            # optional for local models
```

An **Explain this exception** button then appears under each occurrence. Clicking it sends
the exception type and message, the stack trace and the throw-site source to your model.
Flare redacts tokens, passwords, connection-string secrets, emails and IP addresses first,
but pattern matching can miss things, so use a local model if the code is sensitive. The
prompt is capped at `Ai__MaxInputChars` (12000) and the answer at `Ai__MaxOutputTokens`
(800). Each request is in the audit log and the redacted prompt is logged at Debug.

## Filter logs and traces in plain English (optional)

With the same `Ai__*` settings, the Logs and Traces pages show an **Ask AI** box. Type something like
"5xx on checkout in the last hour, excluding health checks" and Flare sets the time range,
services, severity, text search and attribute filters for you. On Traces it can also build a
structural query ("checkout traces where the payments span failed"). The model only proposes
filters in a fixed vocabulary, never SQL. Flare checks the proposal and drops anything invalid,
then shows the result as the normal editable filters, so you can adjust it and learn the UI.
The request and your service names are sent to the model (redacted); log and trace data are not.
If a part of your request couldn't be expressed, a note under the box says so.

## When a frame isn't linked

Flare leaves a frame as plain text rather than guess:

- the path is absolute and doesn't start with the configured prefix;
- the location is a bare file name with no directory, as Java frames are;
- the occurrence has no commit and the service has no fallback branch.

## See also

- [Architecture decision: ADR-0095](../../docs-internal/adr/0095-exception-source-links.md)
- [ADR-0096](../../docs-internal/adr/0096-inline-exception-source.md)
- [ADR-0103](../../docs-internal/adr/0103-explain-exception-llm.md)
