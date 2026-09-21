// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PipelineRuleModels.cs`'s `PipelineRule`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself: it has
// its own `DateTimeOffset` `CreatedAt`/`UpdatedAt` and nests `LogFilter` (itself blocked -
// see `$lib/memorypack/LogFilter.ts`'s header comment) and `IReadOnlyList<PipelineRuleAction>`
// (a list, same reason blocks the generator here even though `PipelineRuleAction` itself
// *is* generated - see `$lib/generated/memorypack/PipelineRuleAction.ts`).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { PipelineRuleAction } from '$lib/generated/memorypack/PipelineRuleAction.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';
import { LogFilter } from '$lib/memorypack/LogFilter';

export class PipelineRule {
	id: string;
	name: string | null;
	description: string | null;
	enabled: boolean;
	condition: LogFilter | null;
	actions: (PipelineRuleAction | null)[] | null;
	createdAt: Date;
	updatedAt: Date;

	constructor() {
		this.id = '00000000-0000-0000-0000-000000000000';
		this.name = null;
		this.description = null;
		this.enabled = false;
		this.condition = null;
		this.actions = null;
		this.createdAt = new Date(0);
		this.updatedAt = new Date(0);
	}

	static serialize(value: PipelineRule | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: PipelineRule | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(8);
		writer.writeGuid(value.id);
		writer.writeString(value.name);
		writer.writeString(value.description);
		writer.writeBoolean(value.enabled);
		LogFilter.serializeCore(writer, value.condition);
		writer.writeArray(value.actions, (writer, x) => PipelineRuleAction.serializeCore(writer, x));
		writeDateTimeOffset(writer, value.createdAt);
		writeDateTimeOffset(writer, value.updatedAt);
	}

	static serializeArray(value: (PipelineRule | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (PipelineRule | null)[] | null): void {
		writer.writeArray(value, (writer, x) => PipelineRule.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): PipelineRule | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): PipelineRule | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new PipelineRule();
		if (count == 8) {
			value.id = reader.readGuid();
			value.name = reader.readString();
			value.description = reader.readString();
			value.enabled = reader.readBoolean();
			value.condition = LogFilter.deserializeCore(reader);
			value.actions = reader.readArray((reader) => PipelineRuleAction.deserializeCore(reader));
			value.createdAt = readDateTimeOffset(reader);
			value.updatedAt = readDateTimeOffset(reader);
		} else if (count > 8) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.id = reader.readGuid();
			if (count == 1) return value;
			value.name = reader.readString();
			if (count == 2) return value;
			value.description = reader.readString();
			if (count == 3) return value;
			value.enabled = reader.readBoolean();
			if (count == 4) return value;
			value.condition = LogFilter.deserializeCore(reader);
			if (count == 5) return value;
			value.actions = reader.readArray((reader) => PipelineRuleAction.deserializeCore(reader));
			if (count == 6) return value;
			value.createdAt = readDateTimeOffset(reader);
			if (count == 7) return value;
			value.updatedAt = readDateTimeOffset(reader);
			if (count == 8) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (PipelineRule | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (PipelineRule | null)[] | null {
		return reader.readArray((reader) => PipelineRule.deserializeCore(reader));
	}
}
