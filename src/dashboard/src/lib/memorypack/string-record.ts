// Hand-written write/read helpers for a `Map<string,string>`-shaped wire value
// (an `IReadOnlyDictionary<string, string>` on the C# side) that decode into a plain
// `Record<string, string>` instead of a `Map` - MemoryPack's generated
// `MemoryPackWriter.writeMap`/`MemoryPackReader.readMap` (used for every other
// collection-typed member) always produce/expect a `Map`, which every one of this
// dashboard's consumers (`api.ts`/`traces-api.ts`/`metrics-api.ts`) immediately converted
// to a `Record` right after decoding anyway, via a `key ?? ''`/`value ?? ''` loop - see
// docs-internal/investigations/memorypack-vs-json-benchmark.md's Finding 2 follow-up,
// recommendation #1. Building the `Record` directly here - instead of a `Map` that gets
// thrown away one line later at every call site - removes both the `Map` construction
// cost (V8's hidden-class/inline-cache-optimized object literals are cheaper to build
// than a `Map`'s separate hash-table bookkeeping) and the second Object-copy pass every
// consumer used to do.
//
// Wire format is unchanged - identical collection-header + key/value string pairs
// `MemoryPackWriter.writeMap`/`MemoryPackReader.readMap` already produce/expect - only
// the in-memory TypeScript representation differs.
//
// Null keys/values (`MemoryPackReader.readString()`'s return type is always
// `string | null`) coalesce to `""`, matching every consumer's former
// `key ?? ''`/`value ?? ''` exactly - real server data never actually writes a null
// dictionary key/value (every `Flare.Api.Model` dictionary member is
// `IReadOnlyDictionary<string, string>`, never `<string?, string?>`), so this is
// defensive, not a behavior change.

import type { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import type { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

export type StringRecord = Record<string, string> | null;

export function writeStringRecord(writer: MemoryPackWriter, value: StringRecord): void {
	if (value == null) {
		writer.writeNullCollectionHeader();
		return;
	}

	const keys = Object.keys(value);
	writer.writeCollectionHeader(keys.length);
	for (let i = 0; i < keys.length; i++) {
		const key = keys[i];
		writer.writeString(key);
		writer.writeString(value[key]);
	}
}

export function readStringRecord(reader: MemoryPackReader): StringRecord {
	const [ok, length] = reader.tryReadCollectionHeader();
	if (!ok) {
		return null;
	}

	const result: Record<string, string> = {};
	for (let i = 0; i < length; i++) {
		const key = reader.readString();
		const value = reader.readString();
		result[key ?? ''] = value ?? '';
	}
	return result;
}
