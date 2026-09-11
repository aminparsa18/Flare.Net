// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/AlertModels.cs`'s `AlertTestResult`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `EvaluatedAt` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment. `conditionKind`/`observedValue` were appended after every pre-existing field,
// same versioning reasoning as `AlertRule.ts`.

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

	constructor() {
		this.observedCount = 0n;
		this.wouldFire = false;
		this.evaluatedAt = new Date(0);
		this.windowSeconds = 0;
		this.conditionKind = 0;
		this.observedValue = null;
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

		writer.writeObjectHeader(6);
		writer.writeUint64(value.observedCount);
		writer.writeBoolean(value.wouldFire);
		writeDateTimeOffset(writer, value.evaluatedAt);
		writer.writeInt32(value.windowSeconds);
		writer.writeInt32(value.conditionKind);
		writer.writeNullableFloat64(value.observedValue);
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
		if (count == 6) {
			value.observedCount = reader.readUint64();
			value.wouldFire = reader.readBoolean();
			value.evaluatedAt = readDateTimeOffset(reader);
			value.windowSeconds = reader.readInt32();
			value.conditionKind = reader.readInt32();
			value.observedValue = reader.readNullableFloat64();
		} else if (count > 6) {
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
		}
		return value;
	}
}
