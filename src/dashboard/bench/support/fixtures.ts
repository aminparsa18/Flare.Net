// Reads the fixture bytes `Flare.Benchmarks export <dir>` wrote: every scenario's response body
// exactly as Flare.Api sends it (System.Text.Json or MemoryPack), uncompressed and per codec.
// scripts/bench.sh runs the export first; FLARE_BENCH_FIXTURES overrides the directory.

import { readFileSync } from 'node:fs';
import path from 'node:path';

export type Format = 'json' | 'memorypack';
export const FORMATS: readonly Format[] = ['json', 'memorypack'];

/** Which dashboard view a scenario feeds, from its name prefix (`logs-page.captured` -> `logs`). */
export type Kind = 'logs' | 'trace' | 'metrics';

export interface ScenarioInfo {
	name: string;
	source: 'captured' | 'synthetic';
	description: string;
	endpoint: string;
	shape: string;
	sizes: Record<Format, Record<string, number>>;
}

export interface Manifest {
	codecs: string[];
	scenarios: ScenarioInfo[];
	request: ScenarioInfo;
}

export const FIXTURES_DIR =
	process.env.FLARE_BENCH_FIXTURES ?? path.resolve(import.meta.dirname, '../../../../artifacts/bench/fixtures');

export function loadManifest(): Manifest {
	try {
		return JSON.parse(readFileSync(path.join(FIXTURES_DIR, 'manifest.json'), 'utf8')) as Manifest;
	} catch (e) {
		throw new Error(
			`No fixtures at ${FIXTURES_DIR}. Run scripts/bench.sh (or \`dotnet run -c Release --project src/Flare.Benchmarks -- export artifacts/bench/fixtures\`) first.`,
			{ cause: e }
		);
	}
}

/** The body bytes, in an ArrayBuffer of exactly that length - what `Response.arrayBuffer()` hands the decoder. */
export function loadBytes(scenario: string, format: Format, codec: string): Uint8Array<ArrayBuffer> {
	const file = readFileSync(path.join(FIXTURES_DIR, scenario, `${format}.${codec}`));
	return new Uint8Array(file.buffer.slice(file.byteOffset, file.byteOffset + file.byteLength));
}

export function kindOf(scenario: string): Kind {
	const prefix = scenario.split('.')[0];
	if (prefix === 'logs-page') return 'logs';
	if (prefix === 'trace' || prefix === 'trace-large') return 'trace';
	if (prefix === 'metrics') return 'metrics';
	throw new Error(`No decoder for scenario '${scenario}' - add its kind to bench/support/fixtures.ts and client.ts.`);
}
