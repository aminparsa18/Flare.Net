// MemoryPack's generated TypeScript decoders (ADR-0016) vs plain `JSON.parse`/
// hand-written interface parsing, for the same `LogEventDto`/`LogSearchResponse`
// response shapes the dashboard actually receives from `POST /api/logs/search` - the
// TypeScript half of docs-internal/planning/roadmap.md's "Flare-specific
// JSON-vs-MemoryPack benchmark" item (see src/Flare.Benchmarks for the .NET half,
// which this deliberately mirrors: a single-row "One" scenario and a full
// `LogSearchQueryBuilder.DefaultPageSize` (200) "Page" scenario, encode and decode).
//
// Uses tinybench directly (not a test framework - see this repo's own choice, made
// when this benchmark was added: the dashboard had no test runner at all yet, and a
// one-off micro-benchmark didn't warrant adding one just to get `bench()`).
//
// Run: npm run bench   (from src/dashboard)

import { Bench } from 'tinybench';
import { LogEventDto } from '$lib/memorypack/LogEventDto';
import { LogSearchResponse } from '$lib/memorypack/LogSearchResponse';
import { buildLogEventDto, buildLogSearchResponsePage, newRandom } from './fixtures';
import { parseLogEventDtoJson, parseLogSearchResponseJson, responseToJsonShape, toJsonShape } from './json-shape';

const PAGE_SIZE = 200;

async function main() {
	const rand = newRandom(42);

	const one = buildLogEventDto(rand);
	const page = buildLogSearchResponsePage(rand, PAGE_SIZE);

	const oneMemoryPack = LogEventDto.serialize(one);
	const oneJsonText = JSON.stringify(toJsonShape(one));
	const pageMemoryPack = LogSearchResponse.serialize(page);
	const pageJsonText = JSON.stringify(responseToJsonShape(page));

	console.log(`One:  MemoryPack ${oneMemoryPack.byteLength} B, JSON ${oneJsonText.length} B (UTF-16 chars)`);
	console.log(`Page: MemoryPack ${pageMemoryPack.byteLength} B, JSON ${pageJsonText.length} B (UTF-16 chars)\n`);

	const bench = new Bench({ time: 500 });

	bench
		.add('MemoryPack_Encode_One', () => {
			LogEventDto.serialize(one);
		})
		.add('Json_Encode_One', () => {
			JSON.stringify(toJsonShape(one));
		})
		.add('MemoryPack_Decode_One', () => {
			LogEventDto.deserialize(oneMemoryPack.buffer as ArrayBuffer);
		})
		.add('Json_Decode_One', () => {
			parseLogEventDtoJson(JSON.parse(oneJsonText));
		})
		.add('MemoryPack_Encode_Page', () => {
			LogSearchResponse.serialize(page);
		})
		.add('Json_Encode_Page', () => {
			JSON.stringify(responseToJsonShape(page));
		})
		.add('MemoryPack_Decode_Page', () => {
			LogSearchResponse.deserialize(pageMemoryPack.buffer as ArrayBuffer);
		})
		.add('Json_Decode_Page', () => {
			parseLogSearchResponseJson(JSON.parse(pageJsonText));
		});

	await bench.run();

	console.table(bench.table());
}

main();
