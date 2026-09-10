// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ServiceCallBreakdownModels.cs`'s
// `ServiceCallBreakdownResponse` field-for-field, in declared order. Can't carry
// `[GenerateTypeScript]` itself: it nests `IReadOnlyList<ExternalCallGroup>` and
// `IReadOnlyList<DatabaseCallGroup>`, which blocks the generator the same way
// `ServiceOverviewResponse`'s own list member does (see that file's own header comment).
// `ExternalCallGroup`/`DatabaseCallGroup` have no such member, so both are real generated
// classes, reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ExternalCallGroup } from '$lib/generated/memorypack/ExternalCallGroup.js';
import { DatabaseCallGroup } from '$lib/generated/memorypack/DatabaseCallGroup.js';

export class ServiceCallBreakdownResponse {
	service: string | null;
	windowMinutes: number;
	externalCalls: (ExternalCallGroup | null)[] | null;
	databaseCalls: (DatabaseCallGroup | null)[] | null;

	constructor() {
		this.service = null;
		this.windowMinutes = 0;
		this.externalCalls = null;
		this.databaseCalls = null;
	}

	static serialize(value: ServiceCallBreakdownResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ServiceCallBreakdownResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeString(value.service);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.externalCalls, (writer, x) => ExternalCallGroup.serializeCore(writer, x));
		writer.writeArray(value.databaseCalls, (writer, x) => DatabaseCallGroup.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ServiceCallBreakdownResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ServiceCallBreakdownResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ServiceCallBreakdownResponse();
		if (count == 4) {
			value.service = reader.readString();
			value.windowMinutes = reader.readInt32();
			value.externalCalls = reader.readArray((reader) => ExternalCallGroup.deserializeCore(reader));
			value.databaseCalls = reader.readArray((reader) => DatabaseCallGroup.deserializeCore(reader));
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.service = reader.readString();
			if (count == 1) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 2) return value;
			value.externalCalls = reader.readArray((reader) => ExternalCallGroup.deserializeCore(reader));
			if (count == 3) return value;
			value.databaseCalls = reader.readArray((reader) => DatabaseCallGroup.deserializeCore(reader));
			if (count == 4) return value;
		}
		return value;
	}
}
