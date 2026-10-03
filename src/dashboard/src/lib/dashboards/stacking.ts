// Stacked-area math for the time series line charts (MetricChart/FormulaChart) - the
// `stacking` option of docs-internal/adr/0086-bar-chart-stacking.md. BarVisualization stacks
// inline because its bars are drawn per bucket; areas need whole per-series bands, so this
// computes them once. Pure: no Svelte, no DOM.

import type { PanelStacking } from "./visualization";

export interface StackInput {
  label: string;
  points: { time: number; raw: number }[];
}

export interface StackedPoint {
  time: number;
  /** Band edges in plot units - the raw value, or 0-100 in percent mode. */
  lower: number;
  upper: number;
  /** The series' real value in this bucket, `null` where it has no point (it then has zero thickness). */
  raw: number | null;
  /** Share of the bucket's total magnitude, 0-100 (set in every mode, used by the tooltip in percent). */
  share: number;
}

export interface StackedLayer {
  label: string;
  points: StackedPoint[];
}

/**
 * Stacks `lines` in order, positives upward and negatives downward from zero. A series
 * missing from a bucket contributes nothing there (rather than leaving a gap in its band,
 * which would pinch every band above it). `percent` divides each bucket by the sum of its
 * absolute values so the stack fills -100..100 (0..100 for non-negative data).
 */
export function stackLines(
  lines: StackInput[],
  bucketTimes: number[],
  stacking: Exclude<PanelStacking, "none">,
): StackedLayer[] {
  const lookups = lines.map(
    (l) => new Map(l.points.map((p) => [p.time, p.raw])),
  );
  const layers: StackedLayer[] = lines.map((l) => ({
    label: l.label,
    points: [],
  }));
  for (const time of bucketTimes) {
    const raws = lookups.map((lookup) => lookup.get(time) ?? null);
    const total = raws.reduce<number>(
      (sum, v) => sum + (v == null ? 0 : Math.abs(v)),
      0,
    );
    const scale = stacking === "percent" ? (total === 0 ? 0 : 100 / total) : 1;
    let pos = 0;
    let neg = 0;
    raws.forEach((raw, i) => {
      const v = (raw ?? 0) * scale;
      const from = v >= 0 ? pos : neg;
      const to = from + v;
      if (v >= 0) pos = to;
      else neg = to;
      layers[i].points.push({
        time,
        lower: Math.min(from, to),
        upper: Math.max(from, to),
        raw,
        share: total === 0 ? 0 : (Math.abs(raw ?? 0) / total) * 100,
      });
    });
  }
  return layers;
}

/** The plot range the layers occupy (always includes 0). */
export function stackExtent(layers: StackedLayer[]): {
  lo: number;
  hi: number;
} {
  let lo = 0;
  let hi = 0;
  for (const layer of layers) {
    for (const p of layer.points) {
      lo = Math.min(lo, p.lower);
      hi = Math.max(hi, p.upper);
    }
  }
  return { lo, hi };
}

/** SVG path for one layer's filled band: the top edge left-to-right, the bottom edge back. */
export function areaPath(
  points: StackedPoint[],
  xFor: (time: number) => number,
  yFor: (value: number) => number,
): string {
  if (points.length === 0) return "";
  const top = points.map(
    (p, i) => `${i === 0 ? "M" : "L"} ${xFor(p.time)} ${yFor(p.upper)}`,
  );
  const bottom = [...points]
    .reverse()
    .map((p) => `L ${xFor(p.time)} ${yFor(p.lower)}`);
  return `${top.join(" ")} ${bottom.join(" ")} Z`;
}

/** SVG path for a layer's top edge only (the visible line on top of the fill). */
export function topEdgePath(
  points: StackedPoint[],
  xFor: (time: number) => number,
  yFor: (value: number) => number,
): string {
  return points
    .map((p, i) => `${i === 0 ? "M" : "L"} ${xFor(p.time)} ${yFor(p.upper)}`)
    .join(" ");
}
