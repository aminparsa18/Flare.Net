// Y-axis scale + tick generation for MetricChart. Two concerns live here:
//
//  1. Unit-aware value formatting, derived from the OTel/UCUM unit string a producer
//     declares on a Metric (`Metric.Unit` - see OtlpMetricsMapper.cs), which
//     Flare.Ingest stores and Flare.Api returns unchanged (ClickHouseMetricRowMapper.cs)
//     - so a raw value queried back is in whatever unit the producer chose, not
//     normalized to any base. Time and bytes get real scaling (ms/s/min.., B/KB/MB..);
//     everything else - including UCUM curly-brace annotations like "{exception}",
//     which name *what's* being counted rather than a physical dimension - is treated
//     as a plain dimensionless count.
//
//  2. "Nice" round tick values (Heckbert's algorithm) so an axis reads 0/10/20/30/40
//     rather than 0/13.7/27.4/41.1 - computed in the *displayed* magnitude (e.g. MB),
//     not the raw declared-unit value, so a byte metric reads "0 / 10 / 20 / 30 MB"
//     rather than "0 / 9.54 / 19.07 / 28.61 MB" (round in bytes, not in MB).
//
// A chart picks ONE scale from its peak value and formats every tick/tooltip in that
// scale, rather than each point re-picking its own - so an axis reads "40 ms / 30 ms /
// ..." rather than mixing "40 ms" with "0.03 s".

interface ScaleStep {
	/** Multiplier from this family's base unit to this scale, e.g. seconds -> ms is 1e3. */
	perBase: number;
	label: string;
}

// Declared-unit -> base-unit multiplier. Base units are seconds and bytes.
const TIME_UNIT_TO_SECONDS: Record<string, number> = {
	ns: 1e-9,
	'µs': 1e-6,
	us: 1e-6,
	ms: 1e-3,
	s: 1,
	min: 60,
	h: 3600,
	d: 86400
};

const BYTE_UNIT_TO_BYTES: Record<string, number> = {
	By: 1,
	kBy: 1e3,
	MBy: 1e6,
	GBy: 1e9,
	TBy: 1e12,
	KiBy: 1024,
	MiBy: 1024 ** 2,
	GiBy: 1024 ** 3,
	TiBy: 1024 ** 4
};

// Ascending `perBase` (i.e. descending physical unit size: h before ns) so pickScale
// can walk from the biggest unit down and stop at the first one that reads >= 1.
const TIME_SCALES: ScaleStep[] = [
	{ perBase: 1 / 3600, label: 'h' },
	{ perBase: 1 / 60, label: 'min' },
	{ perBase: 1, label: 's' },
	{ perBase: 1e3, label: 'ms' },
	{ perBase: 1e6, label: 'µs' },
	{ perBase: 1e9, label: 'ns' }
];

const BYTE_SCALES: ScaleStep[] = [
	{ perBase: 1 / 1024 ** 4, label: 'TB' },
	{ perBase: 1 / 1024 ** 3, label: 'GB' },
	{ perBase: 1 / 1024 ** 2, label: 'MB' },
	{ perBase: 1 / 1024, label: 'KB' },
	{ perBase: 1, label: 'B' }
];

const compactFormat = new Intl.NumberFormat(undefined, { maximumFractionDigits: 1, notation: 'compact' });
const preciseFormat = new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 });

/** Compact notation ("1.4k") from the thousands up; up to 2 decimals below that so sub-1 values ("0.03") stay legible. */
function formatMagnitude(n: number): string {
	return Math.abs(n) >= 1000 ? compactFormat.format(n) : preciseFormat.format(n);
}

/** "{exception}" -> "exception"; null if `unit` isn't a UCUM curly-brace annotation. */
function annotationWord(unit: string): string | null {
	const m = /^\{(.+)\}$/.exec(unit);
	return m ? m[1] : null;
}

function pickScale(scales: ScaleStep[], peakInBase: number): ScaleStep {
	if (!(peakInBase > 0)) return scales[Math.floor(scales.length / 2)];
	for (const scale of scales) {
		if (peakInBase * scale.perBase >= 1) return scale;
	}
	return scales[scales.length - 1];
}

export interface AxisScale {
	/** Multiply a raw value (in the metric's declared unit) by this to get this scale's display magnitude. */
	factor: number;
	/** The unit suffix this scale settled on ("ms", "MB", "%", "" for dimensionless). */
	suffix: string;
}

/**
 * Picks one display scale for a metric's declared unit, sized to `peakAbs` (the
 * largest |raw value| the axis needs to render) - e.g. a metric peaking at 0.03s
 * picks "ms" so ticks read "30 ms" rather than "0.03 s".
 */
export function resolveAxisScale(unit: string | null | undefined, peakAbs: number): AxisScale {
	const u = (unit ?? '').trim();

	// Rate ("<numerator>/<denominator>", e.g. "{request}/s", "By/s") - scale the
	// numerator on its own merits, keep the denominator literal.
	const slash = u.indexOf('/');
	if (slash > 0) {
		const numeratorUnit = u.slice(0, slash).trim();
		const denominator = u.slice(slash + 1).trim() || '?';
		const word = annotationWord(numeratorUnit);
		if (word || numeratorUnit === '' || numeratorUnit === '1') {
			return { factor: 1, suffix: word ? `${word}/${denominator}` : `/${denominator}` };
		}
		const numerator = resolveAxisScale(numeratorUnit, peakAbs);
		return { factor: numerator.factor, suffix: `${numerator.suffix}/${denominator}` };
	}

	const secondsPerUnit = TIME_UNIT_TO_SECONDS[u];
	if (secondsPerUnit !== undefined) {
		const scale = pickScale(TIME_SCALES, peakAbs * secondsPerUnit);
		return { factor: secondsPerUnit * scale.perBase, suffix: scale.label };
	}

	const bytesPerUnit = BYTE_UNIT_TO_BYTES[u];
	if (bytesPerUnit !== undefined) {
		const scale = pickScale(BYTE_SCALES, peakAbs * bytesPerUnit);
		return { factor: bytesPerUnit * scale.perBase, suffix: scale.label };
	}

	if (u === '%') {
		return { factor: 1, suffix: '%' };
	}

	// Dimensionless: no declared unit, UCUM "1" (ratio), or a curly-brace annotation
	// ("{exception}") - the count itself is the whole story, so no suffix.
	if (u === '' || u === '1' || annotationWord(u)) {
		return { factor: 1, suffix: '' };
	}

	// Unrecognized unit (e.g. "Cel", "V") - keep it as a literal suffix rather than
	// silently dropping context.
	return { factor: 1, suffix: u };
}

/** Formats a raw value (in the metric's declared unit) at a scale from `resolveAxisScale`. */
export function formatAtScale(raw: number, scale: AxisScale): string {
	const magnitude = formatMagnitude(raw * scale.factor);
	if (!scale.suffix) return magnitude;
	return scale.suffix === '%' ? `${magnitude}%` : `${magnitude} ${scale.suffix}`;
}

/** Heckbert's "nice numbers" - round step sizes (1/2/5 x 10^n) so ticks read 0/10/20/... rather than 0/13.7/27.4/... */
function niceNumber(value: number, round: boolean): number {
	if (!(value > 0)) return 0;
	const exponent = Math.floor(Math.log10(value));
	const fraction = value / 10 ** exponent;
	let niceFraction: number;
	if (round) {
		if (fraction < 1.5) niceFraction = 1;
		else if (fraction < 3) niceFraction = 2;
		else if (fraction < 7) niceFraction = 5;
		else niceFraction = 10;
	} else {
		if (fraction <= 1) niceFraction = 1;
		else if (fraction <= 2) niceFraction = 2;
		else if (fraction <= 5) niceFraction = 5;
		else niceFraction = 10;
	}
	return niceFraction * 10 ** exponent;
}

// Kills float noise from repeated multiplication (e.g. 0.1 * 3 === 0.30000000000000004).
function roundFloat(n: number): number {
	return Math.round(n * 1e9) / 1e9;
}

export interface AxisTicks {
	/** Raw values (in the metric's declared unit), ascending from 0, to draw gridlines/labels at. */
	values: number[];
	/** The rounded-up ceiling (>= peakAbs, in the metric's declared unit) the top tick lands on - use this as the chart's y-scale max instead of the raw peak, so the top gridline is a round number. */
	max: number;
}

/**
 * Picks ~(targetCount + 1) evenly-spaced round tick values from 0 up to (at least)
 * `peakAbs` (raw, in the metric's declared unit) - "nice" in the *displayed* magnitude
 * (`scale.factor` applied), not the raw unit, so a byte metric reads "0/10/20/30 MB"
 * rather than "0/9.54/19.07/28.61 MB".
 */
export function niceAxisTicks(peakAbs: number, scale: AxisScale, targetCount = 4): AxisTicks {
	const peakDisplay = peakAbs * scale.factor;
	if (!(peakDisplay > 0)) return { values: [0], max: 1 / scale.factor };
	const step = niceNumber(peakDisplay / targetCount, true);
	const count = Math.ceil(peakDisplay / step);
	const maxDisplay = count * step;
	const values = Array.from({ length: count + 1 }, (_, i) => roundFloat((i * step) / scale.factor));
	return { values, max: roundFloat(maxDisplay / scale.factor) };
}
