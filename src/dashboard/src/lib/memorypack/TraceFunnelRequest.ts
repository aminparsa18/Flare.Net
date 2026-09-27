// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/TraceFunnelModels.cs`'s `TraceFunnelRequest`, in
// declared order. Hand-written because `Steps` is an `IReadOnlyList<T>` (see ADR-0016).
// Request-only, so serialize-only.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { TraceFunnelStep } from '$lib/memorypack/TraceFunnelStep';

export class TraceFunnelRequest {
	windowMinutes: number | null = null;
	endUnixMs: bigint | null = null;
	steps: (TraceFunnelStep | null)[] | null = null;

	static serialize(value: TraceFunnelRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		if (value == null) {
			writer.writeNullObjectHeader();
			return writer.toArray();
		}

		writer.writeObjectHeader(3);
		writer.writeNullableInt32(value.windowMinutes);
		writer.writeNullableInt64(value.endUnixMs);
		writer.writeArray(value.steps, (writer, x) => TraceFunnelStep.serializeCore(writer, x));
		return writer.toArray();
	}
}
