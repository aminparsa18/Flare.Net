// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/TraceFunnelModels.cs`'s `TraceFunnelTracesResponse`.
// Hand-written because `Traces` is an `IReadOnlyList<T>` (see ADR-0016); `TraceFunnelTrace`
// itself is generated. Response-only, so deserialize-only.

import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { TraceFunnelTrace } from '$lib/generated/memorypack/TraceFunnelTrace.js';

export class TraceFunnelTracesResponse {
	traces: (TraceFunnelTrace | null)[] | null = null;

	static deserialize(buffer: ArrayBuffer): TraceFunnelTracesResponse | null {
		const reader = new MemoryPackReader(buffer);
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) return null;
		if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		}

		const value = new TraceFunnelTracesResponse();
		if (count == 0) return value;
		value.traces = reader.readArray((reader) => TraceFunnelTrace.deserializeCore(reader));
		return value;
	}
}
