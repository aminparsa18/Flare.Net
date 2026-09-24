// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/AlertModels.cs`'s `AnomalyCondition`
// field-for-field, in declared order. Deliberately hand-written (the C# type carries no
// `[GenerateTypeScript]`) so `source`/`seasonality`/`direction` stay plain ints converted
// through `$lib/memorypack/enums.ts`'s name/int helpers, same as `AlertRule.conditionKind`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

export class AnomalyCondition {
	source: number;
	seasonality: number;
	baselinePeriods: number;
	zScoreThreshold: number;
	direction: number;

	constructor() {
		this.source = 0;
		this.seasonality = 0;
		this.baselinePeriods = 0;
		this.zScoreThreshold = 0;
		this.direction = 0;
	}

	static serialize(value: AnomalyCondition | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: AnomalyCondition | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writer.writeInt32(value.source);
		writer.writeInt32(value.seasonality);
		writer.writeInt32(value.baselinePeriods);
		writer.writeFloat64(value.zScoreThreshold);
		writer.writeInt32(value.direction);
	}

	static deserialize(buffer: ArrayBuffer): AnomalyCondition | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): AnomalyCondition | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new AnomalyCondition();
		if (count == 5) {
			value.source = reader.readInt32();
			value.seasonality = reader.readInt32();
			value.baselinePeriods = reader.readInt32();
			value.zScoreThreshold = reader.readFloat64();
			value.direction = reader.readInt32();
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.source = reader.readInt32();
			if (count == 1) return value;
			value.seasonality = reader.readInt32();
			if (count == 2) return value;
			value.baselinePeriods = reader.readInt32();
			if (count == 3) return value;
			value.zScoreThreshold = reader.readFloat64();
			if (count == 4) return value;
			value.direction = reader.readInt32();
			if (count == 5) return value;
		}
		return value;
	}
}
