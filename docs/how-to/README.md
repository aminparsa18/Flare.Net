# How-to guides

How-to guides are **task-oriented** directions that take the reader through
the steps to solve a real-world problem with Flare.

## Guidelines for writing how-to guides

- Focus on a **specific task**: run standalone, run with Aspire, configure
  a given auth method, run in cluster mode, troubleshoot a deployment.
- Provide a **series of steps** that lead to completion.
- Assume the reader knows **what** they want to do, but not **how**.
- Don't explain concepts inline — link to
  [`../explanation/`](../explanation/) instead.
- Don't restate exact values (ports, env var names, defaults) — link to
  [`../reference/`](../reference/) instead of duplicating a config table.
- One guide, one problem. Flare's current `docs/auth.md` covers 4 auth
  methods in one file because they share enough surrounding structure
  (Managing users, Backups) to justify it — that's a deliberate exception,
  not the default; most tasks should be their own file.

## Structure template

1. Title: "How to [accomplish X]"
2. Prerequisites (if any)
3. Steps with clear actions
4. Verification (how to confirm it worked)
5. Troubleshooting (common issues specific to this task)

## Anti-patterns to avoid

- Teaching background concepts instead of showing steps
- Including explanation that belongs in `../explanation/`
- Restating reference tables that belong in `../reference/`
- Addressing multiple unrelated problems in one guide
