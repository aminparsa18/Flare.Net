# ADR-0001: SvelteKit for the dashboard SPA

Status: Accepted
Date: 2026-08-07

## Context

Flare's dashboard is explicitly the product's differentiator ("the dashboard
is the part that matters" — Flare's design principles treat storage as
already solved by ClickHouse, with the effort going into the UI instead).
That put real weight on the frontend stack choice, and it needed deciding
early: `Flare.Api`'s query API contract is shaped by whether the dashboard
is a server-rendered component model or a client-rendered SPA talking to
plain HTTP/WebSocket.

The dashboard's stated targets — a dense, virtualized log table handling
thousands of rows without pagination stutter, and a live-tail feed that
feels like `tail -f` — are demanding client-side rendering and state
management requirements, not just a UI to add on top of a backend
framework's existing view layer.

## Decision

Built the dashboard as a **SvelteKit** application (Svelte 5 runes, Tailwind
4, shadcn-svelte `mira` style, lucide icons), living at `src/dashboard`. It
is a client-rendered SPA that talks to `Flare.Api` over plain HTTP and
WebSocket (`src/dashboard/src/lib/api.ts`), with no server-side rendering
dependency on the .NET backend.

## Alternatives considered

- **Blazor.** The natural default given the rest of the stack is .NET, and
  was the working assumption before this decision was made explicit. Not
  chosen: the UI ambition here — smooth virtualized tables at scale, a
  live-tail feel — was judged better served by a dedicated SPA framework's
  client-side rendering model than by Blazor's component/render-tree
  approach, particularly given no other part of Flare needed Blazor's
  server-affinity or WASM trade-offs to justify picking it anyway.
- **React or another general-purpose SPA framework.** Not recorded as
  seriously weighed against Svelte specifically in the original design
  discussion; Svelte 5's runes-based reactivity model was the stack that
  shipped.

## Consequences

- `Flare.Api` stayed frontend-agnostic, as intended — this decision did not
  require any query API changes, confirming the API/dashboard boundary was
  drawn correctly from the start.
- The dashboard is a separate, independently built/deployed project
  (`src/dashboard`), not embedded in an ASP.NET Core host's view pipeline.
- Any future frontend work (new dashboard pages, component patterns) should
  follow Svelte 5 runes conventions, not introduce a second frontend
  paradigm alongside it.

## Related documentation

- `docs/explanation/architecture.md` (once created — Phase 3+ of the
  documentation migration)
- `src/dashboard/README.md` — local dev loop for the dashboard project