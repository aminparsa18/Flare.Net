// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/DashboardModels.cs`'s `DashboardRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `LayoutJson` is a `JsonElement` - see `Dashboard.ts`'s header comment for the full
// explanation and the raw-JSON-text wire format this hand-writes against.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

export class DashboardRequest {
	name: string | null;
	description: string | null;
	layoutJson: unknown;
	tags: (string | null)[] | null;

	constructor() {
		this.name = null;
		this.description = null;
		this.layoutJson = null;
		this.tags = null;
	}

	static serialize(value: DashboardRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: DashboardRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeString(value.name);
		writer.writeString(value.description);
		writer.writeString(JSON.stringify(value.layoutJson ?? null));
		writer.writeArray(value.tags, (writer, x) => writer.writeString(x));
	}

	static deserialize(buffer: ArrayBuffer): DashboardRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): DashboardRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new DashboardRequest();
		if (count == 4) {
			value.name = reader.readString();
			value.description = reader.readString();
			value.layoutJson = JSON.parse(reader.readString() ?? 'null');
			value.tags = reader.readArray((reader) => reader.readString());
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.name = reader.readString();
			if (count == 1) return value;
			value.description = reader.readString();
			if (count == 2) return value;
			value.layoutJson = JSON.parse(reader.readString() ?? 'null');
			if (count == 3) return value;
			value.tags = reader.readArray((reader) => reader.readString());
			if (count == 4) return value;
		}
		return value;
	}
}
