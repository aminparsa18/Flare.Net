// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PipelineRuleModels.cs`'s `PipelineRulePreviewResult`.
// Can't carry `[GenerateTypeScript]` itself because its `matches` member's type,
// `PipelineRulePreviewMatch`, is hand-written - see that file's header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { PipelineRulePreviewMatch } from '$lib/memorypack/PipelineRulePreviewMatch';

export class PipelineRulePreviewResult {
	sampledCount: number;
	changedCount: number;
	matches: (PipelineRulePreviewMatch | null)[] | null;

	constructor() {
		this.sampledCount = 0;
		this.changedCount = 0;
		this.matches = null;
	}

	static serialize(value: PipelineRulePreviewResult | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: PipelineRulePreviewResult | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeInt32(value.sampledCount);
		writer.writeInt32(value.changedCount);
		writer.writeArray(value.matches, (writer, x) => PipelineRulePreviewMatch.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): PipelineRulePreviewResult | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): PipelineRulePreviewResult | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new PipelineRulePreviewResult();
		if (count == 3) {
			value.sampledCount = reader.readInt32();
			value.changedCount = reader.readInt32();
			value.matches = reader.readArray((reader) => PipelineRulePreviewMatch.deserializeCore(reader));
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.sampledCount = reader.readInt32();
			if (count == 1) return value;
			value.changedCount = reader.readInt32();
			if (count == 2) return value;
			value.matches = reader.readArray((reader) => PipelineRulePreviewMatch.deserializeCore(reader));
			if (count == 3) return value;
		}
		return value;
	}
}
