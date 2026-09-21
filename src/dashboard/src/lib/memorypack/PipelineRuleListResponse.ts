// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PipelineRuleModels.cs`'s `PipelineRuleListResponse`.
// Can't carry `[GenerateTypeScript]` itself because its one member's type, `PipelineRule`,
// is hand-written - see `PipelineRule.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { PipelineRule } from '$lib/memorypack/PipelineRule';

export class PipelineRuleListResponse {
	rules: (PipelineRule | null)[] | null;

	constructor() {
		this.rules = null;
	}

	static serialize(value: PipelineRuleListResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: PipelineRuleListResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.rules, (writer, x) => PipelineRule.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): PipelineRuleListResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): PipelineRuleListResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new PipelineRuleListResponse();
		if (count == 1) {
			value.rules = reader.readArray((reader) => PipelineRule.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.rules = reader.readArray((reader) => PipelineRule.deserializeCore(reader));
		}
		return value;
	}
}
