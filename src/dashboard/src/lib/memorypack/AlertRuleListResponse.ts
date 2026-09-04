// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/AlertModels.cs`'s `AlertRuleListResponse`. Can't
// carry `[GenerateTypeScript]` itself because its one member's type, `AlertRule`, is
// hand-written - see `AlertRule.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { AlertRule } from '$lib/memorypack/AlertRule';

export class AlertRuleListResponse {
	rules: (AlertRule | null)[] | null;

	constructor() {
		this.rules = null;
	}

	static serialize(value: AlertRuleListResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: AlertRuleListResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.rules, (writer, x) => AlertRule.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): AlertRuleListResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): AlertRuleListResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new AlertRuleListResponse();
		if (count == 1) {
			value.rules = reader.readArray((reader) => AlertRule.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.rules = reader.readArray((reader) => AlertRule.deserializeCore(reader));
		}
		return value;
	}
}
