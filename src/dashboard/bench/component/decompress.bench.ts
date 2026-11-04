// Component: client-side decompression per scenario, format and codec (Node's native zlib, standing
// in for the browser network stack's own native Content-Encoding decoder).

import { decompress } from '../support/client.ts';
import { FORMATS, loadBytes, loadManifest } from '../support/fixtures.ts';
import { runSuite, type Case } from '../support/runner.ts';

const manifest = loadManifest();
const cases: Case[] = [];
for (const scenario of manifest.scenarios) {
	for (const format of FORMATS) {
		for (const codec of manifest.codecs.filter((c) => c !== 'none')) {
			const body = loadBytes(scenario.name, format, codec);
			cases.push({
				name: `${scenario.name}/${format}/${codec}`,
				tags: { scenario: scenario.name, format, codec, op: 'decompress' },
				fn: () => decompress(codec, body)
			});
		}
	}
}

await runSuite('decompress', cases);
