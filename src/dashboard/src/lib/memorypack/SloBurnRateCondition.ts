// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SloModels.cs`'s `SloBurnRateCondition`
// field-for-field, in declared order (the C# type carries no `[GenerateTypeScript]`, same
// as `AnomalyCondition.ts`).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

export class SloBurnRateCondition {
	sloId: string;
	longWindowSeconds: number;
	shortWindowSeconds: number;
	burnRateThreshold: number;

	constructor() {
		this.sloId = '00000000-0000-0000-0000-000000000000';
		this.longWindowSeconds = 0;
		this.shortWindowSeconds = 0;
		this.burnRateThreshold = 0;
	}

	static serialize(value: SloBurnRateCondition | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SloBurnRateCondition | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeGuid(value.sloId);
		writer.writeInt32(value.longWindowSeconds);
		writer.writeInt32(value.shortWindowSeconds);
		writer.writeFloat64(value.burnRateThreshold);
	}

	static deserialize(buffer: ArrayBuffer): SloBurnRateCondition | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SloBurnRateCondition | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SloBurnRateCondition();
		if (count == 4) {
			value.sloId = reader.readGuid();
			value.longWindowSeconds = reader.readInt32();
			value.shortWindowSeconds = reader.readInt32();
			value.burnRateThreshold = reader.readFloat64();
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.sloId = reader.readGuid();
			if (count == 1) return value;
			value.longWindowSeconds = reader.readInt32();
			if (count == 2) return value;
			value.shortWindowSeconds = reader.readInt32();
			if (count == 3) return value;
			value.burnRateThreshold = reader.readFloat64();
			if (count == 4) return value;
		}
		return value;
	}
}
