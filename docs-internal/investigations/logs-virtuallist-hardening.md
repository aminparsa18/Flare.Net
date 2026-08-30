# Investigation: Logs page `VirtualList` hardening

Date: 2026-08-17, fixes shipped 2026-08-18
Related: `docs/explanation/architecture.md` (Logs page tour)

## Problem statement

The Logs page's virtualized table
(`src/dashboard/src/lib/components/virtual-list/VirtualList.svelte`) has
an access pattern most off-the-shelf virtualizers aren't built for:
live-tail **prepends** new rows to the front of an externally-owned
`items` array while the user may be scrolled anywhere in the list, and
`PAGINATION_CAP`/`LIVE_CAP` **evict** rows from the front once a bound is
hit. This deep-dive started from a real user-visible bug report ("a gap
appears mid-scroll while live, only a reload fixes it") and expanded into
a broader hardening pass once the root cause was found.

## What was tried

Three off-the-shelf virtualizer libraries were swapped in as replacements
for the hand-rolled component, in turn: `@tanstack/svelte-virtual`, then
`@humanspeak/svelte-virtual-list`. Each hit the same wall: **none of them
reconcile scroll position against an *externally*-owned array being
prepended to** — they only handle changes to the visible range that they
themselves drove (e.g. the user scrolling), not a silent mutation of the
underlying data from outside. None fit this specific access pattern.

## Root cause

Ended back on the hand-rolled component. The actual root cause of the
"gap appears mid-scroll" report was **`overflow-anchor` fighting the
manual scroll-compensation effect**: the browser's native scroll anchoring
and the component's own bounded-key-scan compensation effect were both
trying to adjust `scrollTop` in response to the same DOM mutation,
disagreeing. Fixed with `overflow-anchor: none` plus a bounded-key-scan
scroll-compensation effect that explicitly handles both live-tail prepend
and cap-driven eviction from the front.

## Findings ported from library source (not their README)

A follow-up read of `@humanspeak/svelte-virtual-list`'s actual source
surfaced concrete techniques worth porting, even though the library
itself didn't fit. Its height-cache, block-sums, per-item
`ResizeObserver`, grid detection, and orientation-switching are all
irrelevant here (Flare's list uses a fixed row height, sidestepping
everything that library built for *unknown*/measured row heights) — but
five specific hardening items were worth adopting on their own merits:

1. **Keyboard accessibility, previously entirely absent.** `role="region"`
   + `aria-label` + `tabindex="0"` on the scrollable viewport, a keydown
   handler (arrows/PageUp/PageDown/Home/End, fixed-px line step —
   deliberately *not* derived from `itemHeight`, matching how native
   scroll behaves) that checks "is this even a scroll key" before
   touching any layout property so an unrelated keypress never forces a
   stray reflow, and an *inward*-drawn focus ring
   (`outline-offset: -2px`, since the viewport clips outward outlines)
   keyed off the ARIA attributes rather than a class name so it survives
   a future `class` override. Svelte's a11y linter flags
   `role="region"` + tabindex/keydown as "non-interactive" by default —
   suppressed with `svelte-ignore` comments citing the ARIA APG
   scrollable-region pattern this actually follows.
2. **No guard against a bogus zero-height `ResizeObserver` reading.** A
   transient `0` mid-animation/tab-switch/detach-reattach would have
   collapsed the visible range to nothing for a frame. Fixed: non-finite/
   `<= 0`/unchanged readings are ignored, keeping the last known-good
   height.
3. **Dev-mode-only safety nets, directly relevant to the bug class this
   whole investigation was about.** A duplicate-`getKey` assertion (a
   plain `Set`, not a reactive Svelte collection — humanspeak's own
   source notes a reactive one caused a ~10s stall on a 10k-item list from
   capturing a stack trace per key) and a "same `scrollTop` written more
   than 10x in 1s" canary as a cheap feedback-loop detector, both funneled
   through one `writeScrollTop()`/duplicate-key `$effect` gated on a
   build-time-inlined `DEV` constant — verified dead-code-eliminated from
   the production bundle (checked against the actual built client chunks,
   not assumed from the gate alone).
4. **No validation on the `itemHeight` prop.** A `safeItemHeight` derived
   value now validates once at the single point `totalHeight`/
   `visibleCount`/`startIndex`/`offsetY`/the scroll-compensation math all
   read from, falling back to `1px` (keeps the math finite — a
   misconfigured row height now renders visibly squashed instead of
   invisibly `NaN`) with a dev-only `console.error` so the caller
   notices. The fallback itself applies unconditionally; only the warning
   is dev-gated.

## Conclusion

All five items from this backlog shipped 2026-08-18. The hand-rolled
`VirtualList` component was kept rather than replaced — no off-the-shelf
library handled the external-prepend access pattern this page actually
needs — but came out of this pass with the accessibility, robustness, and
dev-diagnostic properties a mature virtualizer library would have had, by
deliberately reading past those libraries' READMEs into their source for
techniques that transfer even when the library itself doesn't fit.

## Unresolved / follow-ups

None named in the source material — all five backlog items are described
as shipped and closed.