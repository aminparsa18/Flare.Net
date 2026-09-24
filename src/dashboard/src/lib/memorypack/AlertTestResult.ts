// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/AlertModels.cs`'s `AlertTestResult`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `EvaluatedAt` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment. `conditionKind`/`observedValue`/`noData` were appended after every pre-existing field,
// same versioning reasoning as `AlertRule.ts`.
// `baselineMean`/`zScore`/`baselineSampleCount` were appended after `noData`, same reasoning (ADR-0048).
// `insufficientData`/`dataPointCount` were appended after `baselineSampleCount`, same reasoning (ADR-0050).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class AlertTestResult {
	observedCount: bigint;
	wouldFire: boolean;
	evaluatedAt: Date;
	windowSeconds: number;
	conditionKind: number;
	observedValue: number | null;
	noData: boolean;
	baselineMean: number | null;
	zScore: number | null;
	baselineSampleCount: number;
	insufficientData: boolean;
	dataPointCount: bigint | null;

	constructor() {
		this.observedCount = 0n;
		this.wouldFire = false;
		this.evaluatedAt = new Date(0);
		this.windowSeconds = 0;
		this.conditionKind = 0;
		this.observedValue = null;
		this.noData = false;
		this.baselineMean = null;
		this.zScore = null;
		this.baselineSampleCount = 0;
		this.insufficientData = false;
		this.dataPointCount = null;
	}

	static serialize(value: AlertTestResult | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: AlertTestResult | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(12);
		writer.writeUint64(value.observedCount);
		writer.writeBoolean(value.wouldFire);
		writeDateTimeOffset(writer, value.evaluatedAt);
		writer.writeInt32(value.windowSeconds);
		writer.writeInt32(value.conditionKind);
		writer.writeNullableFloat64(value.observedValue);
		writer.writeBoolean(value.noData);
		writer.writeNullableFloat64(value.baselineMean);
		writer.writeNullableFloat64(value.zScore);
		writer.writeInt32(value.baselineSampleCount);
		writer.writeBoolean(value.insufficientData);
		writer.writeNullableUint64(value.dataPointCount);
	}

	static deserialize(buffer: ArrayBuffer): AlertTestResult | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): AlertTestResult | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new AlertTestResult();
		if (count == 12) {
			value.observedCount = reader.readUint64();
			value.wouldFire = reader.readBoolean();
			value.evaluatedAt = readDateTimeOffset(reader);
			value.windowSeconds = reader.readInt32();
			value.conditionKind = reader.readInt32();
			value.observedValue = reader.readNullableFloat64();
			value.noData = reader.readBoolean();
			value.baselineMean = reader.readNullableFloat64();
			value.zScore = reader.readNullableFloat64();
			value.baselineSampleCount = reader.readInt32();
			value.insufficientData = reader.readBoolean();
			value.dataPointCount = reader.readNullableUint64();
		} else if (count > 12) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.observedCount = reader.readUint64();
			if (count == 1) return value;
			value.wouldFire = reader.readBoolean();
			if (count == 2) return value;
			value.evaluatedAt = readDateTimeOffset(reader);
			if (count == 3) return value;
			value.windowSeconds = reader.readInt32();
			if (count == 4) return value;
			value.conditionKind = reader.readInt32();
			if (count == 5) return value;
			value.observedValue = reader.readNullableFloat64();
			if (count == 6) return value;
			value.noData = reader.readBoolean();
			if (count == 7) return value;
			value.baselineMean = reader.readNullableFloat64();
			if (count == 8) return value;
			value.zScore = reader.readNullableFloat64();
			if (count == 9) return value;
			value.baselineSampleCount = reader.readInt32();
			if (count == 10) return value;
			value.insufficientData = reader.readBoolean();
			if (count == 11) return value;
			value.dataPointCount = reader.readNullableUint64();
			if (count == 12) return value;
		}
		return value;
	}
}
