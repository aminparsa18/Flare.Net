// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/AlertModels.cs`'s `AlertRule` field-for-field, in
// declared order. Can't carry `[GenerateTypeScript]` itself: it has its own `DateTimeOffset`
// `CreatedAt`/`UpdatedAt` and nests `LogFilter` (itself blocked - see
// `$lib/memorypack/LogFilter.ts`'s header comment). `Threshold` (`AlertThreshold`) has
// neither problem, so it's a real generated class, reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { AlertThreshold } from '$lib/generated/memorypack/AlertThreshold.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';
import { LogFilter } from '$lib/memorypack/LogFilter';

export class AlertRule {
	id: string;
	name: string | null;
	description: string | null;
	enabled: boolean;
	condition: LogFilter | null;
	threshold: AlertThreshold | null;
	windowSeconds: number;
	cooldownSeconds: number;
	webhookUrl: string | null;
	telegramBotToken: string | null;
	telegramChatId: string | null;
	emailTo: string | null;
	createdAt: Date;
	updatedAt: Date;

	constructor() {
		this.id = '00000000-0000-0000-0000-000000000000';
		this.name = null;
		this.description = null;
		this.enabled = false;
		this.condition = null;
		this.threshold = null;
		this.windowSeconds = 0;
		this.cooldownSeconds = 0;
		this.webhookUrl = null;
		this.telegramBotToken = null;
		this.telegramChatId = null;
		this.emailTo = null;
		this.createdAt = new Date(0);
		this.updatedAt = new Date(0);
	}

	static serialize(value: AlertRule | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: AlertRule | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(14);
		writer.writeGuid(value.id);
		writer.writeString(value.name);
		writer.writeString(value.description);
		writer.writeBoolean(value.enabled);
		LogFilter.serializeCore(writer, value.condition);
		AlertThreshold.serializeCore(writer, value.threshold);
		writer.writeInt32(value.windowSeconds);
		writer.writeInt32(value.cooldownSeconds);
		writer.writeString(value.webhookUrl);
		writer.writeString(value.telegramBotToken);
		writer.writeString(value.telegramChatId);
		writer.writeString(value.emailTo);
		writeDateTimeOffset(writer, value.createdAt);
		writeDateTimeOffset(writer, value.updatedAt);
	}

	static serializeArray(value: (AlertRule | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (AlertRule | null)[] | null): void {
		writer.writeArray(value, (writer, x) => AlertRule.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): AlertRule | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): AlertRule | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new AlertRule();
		if (count == 14) {
			value.id = reader.readGuid();
			value.name = reader.readString();
			value.description = reader.readString();
			value.enabled = reader.readBoolean();
			value.condition = LogFilter.deserializeCore(reader);
			value.threshold = AlertThreshold.deserializeCore(reader);
			value.windowSeconds = reader.readInt32();
			value.cooldownSeconds = reader.readInt32();
			value.webhookUrl = reader.readString();
			value.telegramBotToken = reader.readString();
			value.telegramChatId = reader.readString();
			value.emailTo = reader.readString();
			value.createdAt = readDateTimeOffset(reader);
			value.updatedAt = readDateTimeOffset(reader);
		} else if (count > 14) {
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
			value.threshold = AlertThreshold.deserializeCore(reader);
			if (count == 6) return value;
			value.windowSeconds = reader.readInt32();
			if (count == 7) return value;
			value.cooldownSeconds = reader.readInt32();
			if (count == 8) return value;
			value.webhookUrl = reader.readString();
			if (count == 9) return value;
			value.telegramBotToken = reader.readString();
			if (count == 10) return value;
			value.telegramChatId = reader.readString();
			if (count == 11) return value;
			value.emailTo = reader.readString();
			if (count == 12) return value;
			value.createdAt = readDateTimeOffset(reader);
			if (count == 13) return value;
			value.updatedAt = readDateTimeOffset(reader);
			if (count == 14) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (AlertRule | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (AlertRule | null)[] | null {
		return reader.readArray((reader) => AlertRule.deserializeCore(reader));
	}
}
