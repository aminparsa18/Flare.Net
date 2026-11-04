// Component: response decode in the dashboard, per scenario and format, on uncompressed bytes.
//   decode     - bytes -> parsed object (JSON: TextDecoder + JSON.parse; MemoryPack: generated decoder)
//   decode+map - bytes -> the UI types `src/lib/*-api.ts` return (see bench/support/client.ts)

import { assertEquivalent, decodeAndMap, decodeOnly } from '../support/client.ts';
import { FORMATS, kindOf, loadBytes, loadManifest } from '../support/fixtures.ts';
import { runSuite, type Case } from '../support/runner.ts';

const cases: Case[] = [];
for (const scenario of loadManifest().scenarios) {
	const kind = kindOf(scenario.name);
	const bytes = { json: loadBytes(scenario.name, 'json', 'none'), memorypack: loadBytes(scenario.name, 'memorypack', 'none') };
	assertEquivalent(scenario.name, kind, bytes.json, bytes.memorypack);

	for (const format of FORMATS) {
		const body = bytes[format];
		cases.push({
			name: `${scenario.name}/${format}/decode`,
			tags: { scenario: scenario.name, format, op: 'decode' },
			fn: () => decodeOnly(kind, format, body)
		});
		cases.push({
			name: `${scenario.name}/${format}/decode+map`,
			tags: { scenario: scenario.name, format, op: 'decode+map' },
			fn: () => decodeAndMap(kind, format, body)
		});
	}
}

await runSuite('decode', cases);
