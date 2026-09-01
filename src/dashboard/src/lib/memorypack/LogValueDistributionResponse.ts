// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogValueDistributionRequest.cs`'s
// `LogValueDistributionResponse`. Can't carry `[GenerateTypeScript]` itself because its one
// member's type, `LogValueDistributionPoint`, has a `DateTimeOffset` member - see
// `LogValueDistributionPoint.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogValueDistributionPoint } from '$lib/memorypack/LogValueDistributionPoint';

export class LogValueDistributionResponse {
	points: (LogValueDistributionPoint | null)[] | null;

	constructor() {
		this.points = null;
	}

	static serialize(value: LogValueDistributionResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogValueDistributionResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.points, (writer, x) => LogValueDistributionPoint.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): LogValueDistributionResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogValueDistributionResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogValueDistributionResponse();
		if (count == 1) {
			value.points = reader.readArray((reader) => LogValueDistributionPoint.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.points = reader.readArray((reader) => LogValueDistributionPoint.deserializeCore(reader));
		}
		return value;
	}
}
