# ADR-0267: Browser replay: canvas content

Status: Accepted

Date: 2026-10-21

## Context

[ADR-0264](0264-browser-session-replay.md) records the DOM. A `<canvas>` holds pixels, not markup, so charts, maps, drawings and
games replay as an empty box. [ADR-0266](0266-browser-replay-console-network.md) left canvas open.

rrweb has its own canvas recording (`recordCanvas`) and replay (`UNSAFE_replayCanvas`). The replay half is the problem: it
turns the replay iframe's sandbox from `allow-same-origin` to `allow-same-origin allow-scripts` and rebuilds the page with
rrweb-snapshot's protections off. A recording is whatever a client holding an ingest key posts, and that key ships in public
JavaScript, so anyone can post a snapshot with an `onerror` attribute. With scripts enabled in a frame that shares the
dashboard's origin, that is stored XSS against whoever opens the session, with their dashboard cookie.

## Decision

- **Do not turn on `UNSAFE_replayCanvas`.** The replay frame stays on `allow-same-origin` alone, which is what keeps a
  hostile recording inert.
- **The recorder sends canvas pictures as ordinary custom events** (`tag: 'flare-canvas'`, payload `{ id, w, h, src }`),
  through `record.addCustomEvent`. `id` is the canvas's rrweb node id; `src` is a WebP data URL (PNG where the browser cannot
  encode WebP). Custom events are already stored, buffered by the `onError` rolling buffer and counted by the byte caps, so the
  server and the stored format do not change.
- **The dashboard paints them itself.** The Replayer emits `custom-event` while playing and also while seeking, so a seek lands
  on the latest frame before it. The handler decodes the picture in the dashboard's own window and draws it onto the canvas
  element found through the Replayer's mirror. A per-canvas sequence number drops a slow decode of an old frame that finished
  after a newer one.
- **The dashboard trusts nothing in the event.** `parseCanvasFrame` accepts only an integer id and a base64 `webp`, `png` or
  `jpeg` data URL under 400 000 characters. An SVG data URL is refused.
- **Off by default, opt-in with `captureCanvas`** (`true` is 1 frame per second, a number is 1 to 10). A pixel cannot be masked
  the way text can, so a canvas that draws personal data would be stored as drawn. A canvas inside an element matching
  `blockSelector` or `maskTextSelector` is never recorded, and `maskAllText` turns canvas capture off, because it promises that
  nothing readable is stored.
- **Bounded at the source.** A canvas that has not changed since its last frame sends nothing (`FrameGate`). A frame is scaled
  to 800 px on its longest side, WebP quality 0.5, and dropped above 150 000 characters. A session stops after about 25 MB of
  frames. After each full page snapshot every canvas is sent again, since the snapshot rebuilds them blank; this also makes
  the start of an `onError` recording show its canvases.
- **Pixels are read with `drawImage` onto a scratch canvas inside a `requestAnimationFrame`.** This works for WebGL, where
  `toDataURL` on the canvas itself usually returns blank. A canvas tainted by a cross-origin image throws on read and is skipped.
- **Frames, not drawing commands.** rrweb's 2D recording replays the page's `drawImage`/`fillRect` calls, which is exact but needs
  the replay to execute them in a context it controls, and its WebGL replay is the part that needs scripts. Pictures at a low
  frame rate look like a slideshow for fast animation; they are never wrong about what was shown.

## Consequences

- Canvas content appears in replays without loosening the replay frame's sandbox.
- A canvas-heavy page costs up to the 25 MB session budget in stored events, counted against the key's byte caps (ADR-0265).
- Animation faster than the chosen frame rate is not reproduced; raising the rate costs bytes linearly.
- A recording made without `captureCanvas`, or before this change, replays canvases blank, as before.
