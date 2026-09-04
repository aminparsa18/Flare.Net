// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/AlertModels.cs`'s `AlertHistoryResponse`. Can't
// carry `[GenerateTypeScript]` itself because its one member's type, `AlertHistoryEntry`,
// has a `DateTimeOffset` member - see `AlertHistoryEntry.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { AlertHistoryEntry } from '$lib/memorypack/AlertHistoryEntry';

export class AlertHistoryResponse {
	events: (AlertHistoryEntry | null)[] | null;

	constructor() {
		this.events = null;
	}

	static serialize(value: AlertHistoryResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: AlertHistoryResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.events, (writer, x) => AlertHistoryEntry.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): AlertHistoryResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): AlertHistoryResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new AlertHistoryResponse();
		if (count == 1) {
			value.events = reader.readArray((reader) => AlertHistoryEntry.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.events = reader.readArray((reader) => AlertHistoryEntry.deserializeCore(reader));
		}
		return value;
	}
}
