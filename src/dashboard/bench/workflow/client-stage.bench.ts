// Workflow: the whole client half of a dashboard request, per scenario, format and codec -
// received (possibly compressed) bytes -> decompress -> decode -> map into the UI types.
// The report stacks this with Flare.Benchmarks' ServerStageBenchmarks and a modeled transfer time.

import { assertEquivalent, decodeAndMap, decompress } from '../support/client.ts';
import { FORMATS, kindOf, loadBytes, loadManifest } from '../support/fixtures.ts';
import { runSuite, type Case } from '../support/runner.ts';

const manifest = loadManifest();
const cases: Case[] = [];
for (const scenario of manifest.scenarios) {
	const kind = kindOf(scenario.name);
	assertEquivalent(scenario.name, kind, loadBytes(scenario.name, 'json', 'none'), loadBytes(scenario.name, 'memorypack', 'none'));

	for (const format of FORMATS) {
		for (const codec of manifest.codecs) {
			const body = loadBytes(scenario.name, format, codec);
			cases.push({
				name: `${scenario.name}/${format}/${codec}`,
				tags: { scenario: scenario.name, format, codec, op: 'client' },
				fn: () => decodeAndMap(kind, format, decompress(codec, body))
			});
		}
	}
}

await runSuite('client-stage', cases);
