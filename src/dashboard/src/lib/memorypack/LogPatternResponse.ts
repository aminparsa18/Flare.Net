// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogPatternModels.cs`'s `LogPatternResponse`.
// Can't carry `[GenerateTypeScript]` itself because its one member's type, `LogPatternRow`,
// has `DateTimeOffset` members - see `LogPatternRow.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogPatternRow } from '$lib/memorypack/LogPatternRow';

export class LogPatternResponse {
	patterns: (LogPatternRow | null)[] | null;

	constructor() {
		this.patterns = null;
	}

	static serialize(value: LogPatternResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogPatternResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.patterns, (writer, x) => LogPatternRow.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): LogPatternResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogPatternResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogPatternResponse();
		if (count == 1) {
			value.patterns = reader.readArray((reader) => LogPatternRow.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.patterns = reader.readArray((reader) => LogPatternRow.deserializeCore(reader));
		}
		return value;
	}
}
