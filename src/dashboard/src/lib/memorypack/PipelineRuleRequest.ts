// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PipelineRuleModels.cs`'s `PipelineRuleRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself for the
// same reasons `PipelineRule.ts` documents (nests `LogFilter` and an
// `IReadOnlyList<PipelineRuleAction>`).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { PipelineRuleAction } from '$lib/generated/memorypack/PipelineRuleAction.js';
import { LogFilter } from '$lib/memorypack/LogFilter';

export class PipelineRuleRequest {
	name: string | null;
	description: string | null;
	enabled: boolean | null;
	condition: LogFilter | null;
	actions: (PipelineRuleAction | null)[] | null;

	constructor() {
		this.name = null;
		this.description = null;
		this.enabled = null;
		this.condition = null;
		this.actions = null;
	}

	static serialize(value: PipelineRuleRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: PipelineRuleRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writer.writeString(value.name);
		writer.writeString(value.description);
		writer.writeNullableBoolean(value.enabled);
		LogFilter.serializeCore(writer, value.condition);
		writer.writeArray(value.actions, (writer, x) => PipelineRuleAction.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): PipelineRuleRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): PipelineRuleRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new PipelineRuleRequest();
		if (count == 5) {
			value.name = reader.readString();
			value.description = reader.readString();
			value.enabled = reader.readNullableBoolean();
			value.condition = LogFilter.deserializeCore(reader);
			value.actions = reader.readArray((reader) => PipelineRuleAction.deserializeCore(reader));
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.name = reader.readString();
			if (count == 1) return value;
			value.description = reader.readString();
			if (count == 2) return value;
			value.enabled = reader.readNullableBoolean();
			if (count == 3) return value;
			value.condition = LogFilter.deserializeCore(reader);
			if (count == 4) return value;
			value.actions = reader.readArray((reader) => PipelineRuleAction.deserializeCore(reader));
			if (count == 5) return value;
		}
		return value;
	}
}
