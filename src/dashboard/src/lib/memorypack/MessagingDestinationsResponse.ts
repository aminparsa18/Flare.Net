// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MessagingModels.cs`'s `MessagingDestinationsResponse`,
// in declared order. Can't carry `[GenerateTypeScript]` itself: its members are
// `IReadOnlyList<T>` (see ADR-0016). `MessagingDestination` itself is generated.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MessagingDestination } from '$lib/generated/memorypack/MessagingDestination.js';

export class MessagingDestinationsResponse {
	windowMinutes: number;
	destinations: (MessagingDestination | null)[] | null;
	systems: (string | null)[] | null;
	services: (string | null)[] | null;

	constructor() {
		this.windowMinutes = 0;
		this.destinations = null;
		this.systems = null;
		this.services = null;
	}

	static serialize(value: MessagingDestinationsResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MessagingDestinationsResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.destinations, (writer, x) => MessagingDestination.serializeCore(writer, x));
		writer.writeArray(value.systems, (writer, x) => writer.writeString(x));
		writer.writeArray(value.services, (writer, x) => writer.writeString(x));
	}

	static deserialize(buffer: ArrayBuffer): MessagingDestinationsResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MessagingDestinationsResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MessagingDestinationsResponse();
		if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		}
		if (count == 0) return value;
		value.windowMinutes = reader.readInt32();
		if (count == 1) return value;
		value.destinations = reader.readArray((reader) => MessagingDestination.deserializeCore(reader));
		if (count == 2) return value;
		value.systems = reader.readArray((reader) => reader.readString());
		if (count == 3) return value;
		value.services = reader.readArray((reader) => reader.readString());
		return value;
	}
}
