// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/TraceFunnelModels.cs`'s `TraceFunnelStep`, in
// declared order. Can't carry `[GenerateTypeScript]`: `Attributes` is an `IReadOnlyList<T>`
// (see ADR-0016). Request-only, so serialize-only.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { SpanAttributeFilter } from '$lib/memorypack/SpanAttributeFilter';

export class TraceFunnelStep {
	serviceName: string | null = null;
	spanName: string | null = null;
	attributes: (SpanAttributeFilter | null)[] | null = null;

	static serializeCore(writer: MemoryPackWriter, value: TraceFunnelStep | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeString(value.serviceName);
		writer.writeString(value.spanName);
		writer.writeArray(value.attributes, (writer, x) => SpanAttributeFilter.serializeCore(writer, x));
	}
}
