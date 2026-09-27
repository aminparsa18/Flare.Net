// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/TraceFunnelModels.cs`'s `TraceFunnelTracesRequest`, in
// declared order. Hand-written because `Steps` is an `IReadOnlyList<T>` (see ADR-0016).
// `outcome` is the raw `TraceFunnelOutcome` ordinal (see `traceFunnelOutcomeFromString`).
// Request-only, so serialize-only.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { TraceFunnelStep } from '$lib/memorypack/TraceFunnelStep';

export class TraceFunnelTracesRequest {
	windowMinutes: number | null = null;
	endUnixMs: bigint | null = null;
	steps: (TraceFunnelStep | null)[] | null = null;
	stepIndex = 0;
	outcome = 0;

	static serialize(value: TraceFunnelTracesRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		if (value == null) {
			writer.writeNullObjectHeader();
			return writer.toArray();
		}

		writer.writeObjectHeader(5);
		writer.writeNullableInt32(value.windowMinutes);
		writer.writeNullableInt64(value.endUnixMs);
		writer.writeArray(value.steps, (writer, x) => TraceFunnelStep.serializeCore(writer, x));
		writer.writeInt32(value.stepIndex);
		writer.writeInt32(value.outcome);
		return writer.toArray();
	}
}
