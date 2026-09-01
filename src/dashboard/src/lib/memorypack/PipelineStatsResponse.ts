// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PipelineModels.cs`'s `PipelineStatsResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself: it has its
// own `DateTimeOffset GeneratedAt` member, and every list member is an
// `IReadOnlyList<T>` (see `PipelineServiceBreakdown.ts`'s header comment on why that alone
// already blocks `[GenerateTypeScript]`, independent of DateTimeOffset).
// `PipelineStreamHealth` has neither problem, so it's a real generated class reused here.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { PipelineStreamHealth } from '$lib/generated/memorypack/PipelineStreamHealth.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';
import { PipelineFlushHealth } from '$lib/memorypack/PipelineFlushHealth';
import { PipelineServiceBreakdown } from '$lib/memorypack/PipelineServiceBreakdown';

export class PipelineStatsResponse {
	generatedAt: Date;
	streams: (PipelineStreamHealth | null)[] | null;
	flushWorkers: (PipelineFlushHealth | null)[] | null;
	serviceBreakdowns: (PipelineServiceBreakdown | null)[] | null;

	constructor() {
		this.generatedAt = new Date(0);
		this.streams = null;
		this.flushWorkers = null;
		this.serviceBreakdowns = null;
	}

	static serialize(value: PipelineStatsResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: PipelineStatsResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writeDateTimeOffset(writer, value.generatedAt);
		writer.writeArray(value.streams, (writer, x) => PipelineStreamHealth.serializeCore(writer, x));
		writer.writeArray(value.flushWorkers, (writer, x) => PipelineFlushHealth.serializeCore(writer, x));
		writer.writeArray(value.serviceBreakdowns, (writer, x) => PipelineServiceBreakdown.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): PipelineStatsResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): PipelineStatsResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new PipelineStatsResponse();
		if (count == 4) {
			value.generatedAt = readDateTimeOffset(reader);
			value.streams = reader.readArray((reader) => PipelineStreamHealth.deserializeCore(reader));
			value.flushWorkers = reader.readArray((reader) => PipelineFlushHealth.deserializeCore(reader));
			value.serviceBreakdowns = reader.readArray((reader) => PipelineServiceBreakdown.deserializeCore(reader));
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.generatedAt = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.streams = reader.readArray((reader) => PipelineStreamHealth.deserializeCore(reader));
			if (count == 2) return value;
			value.flushWorkers = reader.readArray((reader) => PipelineFlushHealth.deserializeCore(reader));
			if (count == 3) return value;
			value.serviceBreakdowns = reader.readArray((reader) => PipelineServiceBreakdown.deserializeCore(reader));
			if (count == 4) return value;
		}
		return value;
	}
}
