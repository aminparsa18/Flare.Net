// Deterministic, representative `LogEventDto`/`LogSearchResponse`-shaped fixtures for
// memorypack-vs-json.bench.ts. Mirrors the attribute-bag modeling in
// `src/Flare.Benchmarks/TestData/LogEventDtoFixtures.cs` on the .NET side (same repo,
// same roadmap item - see docs-internal/planning/roadmap.md's "Flare-specific
// JSON-vs-MemoryPack benchmark" item) - not lifted from a captured production payload,
// so treat absolute numbers as directionally representative rather than exact.

import { LogEventDto } from '$lib/memorypack/LogEventDto';
import { LogSearchResponse } from '$lib/memorypack/LogSearchResponse';

/** A tiny seeded PRNG (mulberry32) - deterministic across runs, unlike `Math.random()`. */
function mulberry32(seed: number): () => number {
	let a = seed;
	return () => {
		a |= 0;
		a = (a + 0x6d2b79f5) | 0;
		let t = Math.imul(a ^ (a >>> 15), 1 | a);
		t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
		return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
	};
}

const SERVICE_NAMES = ['checkout-api', 'payments-worker', 'notification-service', 'inventory-api'];
const HTTP_METHODS = ['GET', 'POST', 'PUT', 'DELETE'];
const HTTP_ROUTES = ['/api/orders/{id}', '/api/cart', '/api/payments', '/api/users/{id}/profile'];
const SEVERITY_TEXTS = ['INFO', 'WARN', 'ERROR', 'DEBUG'];

function pick<T>(rand: () => number, values: readonly T[]): T {
	return values[Math.floor(rand() * values.length)];
}

function randomHex(rand: () => number, length: number): string {
	const chars = '0123456789abcdef';
	let out = '';
	for (let i = 0; i < length; i++) {
		out += chars[Math.floor(rand() * 16)];
	}
	return out;
}

function randomGuid(rand: () => number): string {
	const hex = randomHex(rand, 32);
	return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/** One `LogEventDto` instance, ready to feed into `LogSearchResponse.serialize`/`serializeCore`. */
export function buildLogEventDto(rand: () => number): LogEventDto {
	const now = new Date(Date.UTC(2026, 8, 5, 12, 0, 0) + Math.floor(rand() * 86_400_000));
	const serviceName = pick(rand, SERVICE_NAMES);

	const dto = new LogEventDto();
	dto.eventId = randomGuid(rand);
	dto.timestamp = now;
	dto.observedTimestamp = new Date(now.getTime() + Math.floor(rand() * 50));
	dto.ingestedAt = new Date(now.getTime() + 50 + Math.floor(rand() * 100));
	dto.traceId = randomHex(rand, 32);
	dto.spanId = randomHex(rand, 16);
	dto.traceFlags = 1;
	dto.severityText = pick(rand, SEVERITY_TEXTS);
	dto.severityNumber = 1 + Math.floor(rand() * 24);
	dto.serviceName = serviceName;
	dto.body = `Handled ${pick(rand, HTTP_METHODS)} ${pick(rand, HTTP_ROUTES)} in ${Math.floor(rand() * 500)}ms`;
	dto.resourceSchemaUrl = 'https://opentelemetry.io/schemas/1.27.0';
	dto.resourceAttributes = {
		'service.name': serviceName,
		'service.version': `${1 + Math.floor(rand() * 5)}.${Math.floor(rand() * 20)}.${Math.floor(rand() * 20)}`,
		'service.instance.id': randomGuid(rand),
		'deployment.environment': rand() < 0.5 ? 'production' : 'staging',
		'cloud.region': 'us-east-1',
		'host.name': `ip-10-0-${Math.floor(rand() * 255)}-${Math.floor(rand() * 255)}`
	};
	dto.scopeSchemaUrl = 'https://opentelemetry.io/schemas/1.27.0';
	dto.scopeName = `${serviceName}.instrumentation`;
	dto.scopeVersion = '1.4.0';
	dto.scopeAttributes = {};
	dto.logAttributes = {
		'http.request.method': pick(rand, HTTP_METHODS),
		'http.route': pick(rand, HTTP_ROUTES),
		'http.response.status_code': String(200 + Math.floor(rand() * 5) * 100 + Math.floor(rand() * 5)),
		'user.id': randomGuid(rand),
		'request.id': randomGuid(rand)
	};
	dto.eventName = rand() < 0.25 ? 'http.server.request.failed' : '';
	dto.patternId = '';
	dto.patternTemplate = '';
	dto.spanDurationNano = rand() < 0.5 ? null : BigInt(1_000_000 + Math.floor(rand() * 500_000_000));
	return dto;
}

/** A full `LogSearchResponse` page - `count` events, same "genuine batch" framing as the .NET benchmark's `Page`. */
export function buildLogSearchResponsePage(rand: () => number, count: number): LogSearchResponse {
	const response = new LogSearchResponse();
	response.events = Array.from({ length: count }, () => buildLogEventDto(rand));
	response.nextCursor = 'eyJUaW1lc3RhbXAiOiIyMDI2LTA5LTA1VDEyOjAwOjAwWiIsIkV2ZW50SWQiOiJhYmMifQ==';
	return response;
}

export function newRandom(seed = 42): () => number {
	return mulberry32(seed);
}
