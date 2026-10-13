// Boundary helpers between the generated MemoryPack `Map<string | null, string | null>` (an
// `IReadOnlyDictionary<string, string>` on the C# side) and the plain `Record<string, string>`
// every Svelte component and `-api.ts` interface works with.
//
// Null keys/values (`MemoryPackReader.readString()` is always `string | null`) coalesce to `""`.
// Real server data never writes a null dictionary key/value (every `Flare.Api.Model`
// dictionary member is `IReadOnlyDictionary<string, string>`, not `<string?, string?>`), so
// this is defensive, not a behaviour change.

export function mapToRecord(map: Map<string | null, string | null> | null | undefined): Record<string, string> {
	const result: Record<string, string> = {};
	if (map == null) {
		return result;
	}
	for (const [key, value] of map) {
		result[key ?? ''] = value ?? '';
	}
	return result;
}

export function recordToMap(record: Record<string, string> | null | undefined): Map<string, string> | null {
	return record == null ? null : new Map(Object.entries(record));
}
