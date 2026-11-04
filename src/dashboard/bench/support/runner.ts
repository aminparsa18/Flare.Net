// Shared tinybench harness. Every suite file builds a list of cases and calls `runSuite`.
//
//   --json <file>   write the results as JSON (what scripts/bench.sh collects) instead of a table
//   --time <ms>     time budget per case (default 500; scripts/bench.sh --quick passes 150)
//   --filter <s>    only cases whose name contains <s>

import { readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { Bench } from 'tinybench';

export interface Case {
	/** Unique within the suite, e.g. `logs-page.captured/json/gzip-6`. */
	name: string;
	/** Structured copy of what the name encodes, carried into the JSON for the report. */
	tags: Record<string, string>;
	fn: () => unknown;
}

function arg(name: string): string | undefined {
	const i = process.argv.indexOf(`--${name}`);
	return i >= 0 ? process.argv[i + 1] : undefined;
}

export async function runSuite(suite: string, cases: Case[]): Promise<void> {
	const time = Number(arg('time') ?? 500);
	const filter = arg('filter');
	const out = arg('json');
	const selected = filter ? cases.filter((c) => `${suite}/${c.name}`.includes(filter)) : cases;

	const bench = new Bench({ time, warmupTime: Math.max(50, time / 5) });
	for (const c of selected) {
		bench.add(c.name, c.fn);
	}
	await bench.run();

	const rows = bench.tasks.map((task, i) => {
		const r = task.result;
		if (r.state !== 'completed') {
			throw new Error(`${suite}/${task.name} did not complete (${r.state})`, { cause: 'error' in r ? r.error : undefined });
		}
		return {
			suite,
			name: task.name,
			...selected[i].tags,
			meanMs: r.latency.mean,
			p50Ms: r.latency.p50,
			p99Ms: r.latency.p99,
			rmePercent: r.latency.rme,
			samples: r.latency.samplesCount
		};
	});

	if (out) {
		const tinybenchVersion = (JSON.parse(readFileSync(path.resolve(import.meta.dirname, '../../node_modules/tinybench/package.json'), 'utf8')) as { version: string }).version;
		writeFileSync(out, JSON.stringify({ suite, node: process.versions.node, v8: process.versions.v8, tinybench: tinybenchVersion, time, rows }, null, 1));
		console.log(`${suite}: ${rows.length} cases -> ${out}`);
	} else {
		console.table(rows.map(({ name, meanMs, p50Ms, rmePercent, samples }) => ({ name, meanMs, p50Ms, rmePercent, samples })));
	}
}
