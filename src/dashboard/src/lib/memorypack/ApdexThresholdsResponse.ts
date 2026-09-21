// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ApdexModels.cs`'s `ApdexThresholdsResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself: it
// nests `IReadOnlyList<ApdexThresholdDto>`, which blocks the generator the same way
// `ServiceOverviewResponse`'s `Services` member does (see that file's own header
// comment). `ApdexThresholdDto` has no such member, so it's a real generated class,
// reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ApdexThresholdDto } from '$lib/generated/memorypack/ApdexThresholdDto.js';

export class ApdexThresholdsResponse {
	defaultThresholdMs: number;
	overrides: (ApdexThresholdDto | null)[] | null;

	constructor() {
		this.defaultThresholdMs = 0;
		this.overrides = null;
	}

	static serialize(value: ApdexThresholdsResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ApdexThresholdsResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		writer.writeInt32(value.defaultThresholdMs);
		writer.writeArray(value.overrides, (writer, x) => ApdexThresholdDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ApdexThresholdsResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ApdexThresholdsResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ApdexThresholdsResponse();
		if (count == 2) {
			value.defaultThresholdMs = reader.readInt32();
			value.overrides = reader.readArray((reader) => ApdexThresholdDto.deserializeCore(reader));
		} else if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.defaultThresholdMs = reader.readInt32();
			if (count == 1) return value;
			value.overrides = reader.readArray((reader) => ApdexThresholdDto.deserializeCore(reader));
			if (count == 2) return value;
		}
		return value;
	}
}
