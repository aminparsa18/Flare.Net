# Documentation

## Quick links

| Type | Purpose | Start here |
|------|---------|------------|
| [Tutorials](tutorials/) | Learn by doing | For newcomers |
| [How-to guides](how-to/) | Solve a specific problem | For practitioners |
| [Reference](reference/) | Exact technical details | For lookup |
| [Explanation](explanation/) | Understand why Flare works this way | For deeper knowledge |

- **New to Flare?** Start with [tutorials](tutorials/).
- **Need to accomplish a task** (run standalone, configure auth, run a
  cluster)? Check [how-to guides](how-to/).
- **Looking up a CLI command, config key, or schema column?** See
  [reference](reference/).
- **Want to understand why Flare is built this way?** Read
  [explanation](explanation/).

Maintainer-facing documentation (architecture decisions, technical
investigations, the roadmap) lives outside this tree, in
[`../docs-internal/`](../docs-internal/) — see that folder's `README.md` for
the full rule set on what goes where.

## All pages

**Tutorials**
- [Getting started](tutorials/getting-started.md)

**How-to guides**
- [Run standalone](how-to/run-standalone.md)
- [Run with .NET Aspire](how-to/run-with-aspire.md)
- [Run with the CLI](how-to/run-with-cli.md)
- [Configure authentication](how-to/configure-authentication.md)
- [Run in cluster mode](how-to/run-cluster-mode.md)

**Reference**
- [CLI commands](reference/cli-commands.md)
- [Aspire hosting](reference/aspire-hosting.md)
- [Authentication config](reference/authentication-config.md)
- [Clustering config](reference/clustering-config.md)
- [OTLP logger versions](reference/otlp-logger-versions.md)

**Explanation**
- [Architecture](explanation/architecture.md)
- [Clustering](explanation/clustering.md)
- [Authentication model](explanation/authentication-model.md)

## Contributing documentation

When adding new documentation, work out its type first — see
[`../docs-internal/README.md`](../docs-internal/README.md#where-does-new-information-belong)
for the decision tree (it also covers when something is an ADR or
investigation instead of a `docs/` page):

1. **Tutorial**: step-by-step lesson for a beginner
2. **How-to guide**: task-focused instructions for someone who knows what
   they want, not how
3. **Reference**: factual, comprehensive, dry description
4. **Explanation**: context, background, and why