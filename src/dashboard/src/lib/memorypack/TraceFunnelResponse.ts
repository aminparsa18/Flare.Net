// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/TraceFunnelModels.cs`'s `TraceFunnelResponse`, in
// declared order. Hand-written because `Steps` is an `IReadOnlyList<T>` (see ADR-0016);
// `TraceFunnelStepResult` itself is generated. Response-only, so deserialize-only.

import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { TraceFunnelStepResult } from '$lib/generated/memorypack/TraceFunnelStepResult.js';

export class TraceFunnelResponse {
	windowMinutes = 0;
	steps: (TraceFunnelStepResult | null)[] | null = null;

	static deserialize(buffer: ArrayBuffer): TraceFunnelResponse | null {
		const reader = new MemoryPackReader(buffer);
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) return null;
		if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		}

		const value = new TraceFunnelResponse();
		if (count == 0) return value;
		value.windowMinutes = reader.readInt32();
		if (count == 1) return value;
		value.steps = reader.readArray((reader) => TraceFunnelStepResult.deserializeCore(reader));
		return value;
	}
}
