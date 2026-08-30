# Tutorials

Tutorials are **learning-oriented** lessons that take a beginner through a
series of steps to their first success with Flare.

## Guidelines for writing tutorials

- Focus on **learning**, not on accomplishing a task.
- Allow the reader to learn by **doing** — get them started immediately.
- Make sure the tutorial **works** — test it against a real, fresh setup.
- Focus on **concrete steps**, not abstract concepts.
- Provide the **minimum necessary explanation**; link to
  [`../explanation/`](../explanation/) for anything conceptual.
- **Pick one path.** Flare has three legitimate ways to run it (Aspire,
  standalone Docker Compose, CLI) — a tutorial should walk one of them
  start to finish, not offer a choice partway through. `docker compose up`
  is the lowest-prerequisite path and is the current candidate for
  Flare's one tutorial; the other two paths are how-to guides for readers
  who already know which one they want.

## Structure template

1. Introduction (what will be built/learned)
2. Prerequisites
3. Step-by-step instructions
4. What you've learned
5. Next steps (link to relevant how-to guides)

## Anti-patterns to avoid

- Teaching concepts instead of walking through steps
- Offering choices or alternatives mid-tutorial
- Including unnecessary explanation (link out instead)
- Assuming prior knowledge of OpenTelemetry, ClickHouse, or Aspire
