// Component: the request direction of ADR-0015 - encoding a `POST /api/logs/search` body in the
// dashboard. Starts from the same request the .NET side exported (`logs-request.synthetic`):
// MemoryPack serializes the generated class, JSON stringifies the System.Text.Json-shaped object.
// The plain-to-generated filter conversion `searchLogs` also does is excluded from both.

import { LogSearchRequest } from '#lib/generated/memorypack/LogSearchRequest.ts';
import { loadBytes, loadManifest } from '../support/fixtures.ts';
import { runSuite } from '../support/runner.ts';

const name = loadManifest().request.name;
const generated = LogSearchRequest.deserialize(loadBytes(name, 'memorypack', 'none').buffer)!;
const plain: unknown = JSON.parse(new TextDecoder().decode(loadBytes(name, 'json', 'none')));

await runSuite('request-encode', [
	{ name: `${name}/json/encode`, tags: { scenario: name, format: 'json', op: 'encode' }, fn: () => new TextEncoder().encode(JSON.stringify(plain)) },
	{ name: `${name}/memorypack/encode`, tags: { scenario: name, format: 'memorypack', op: 'encode' }, fn: () => LogSearchRequest.serialize(generated) }
]);
