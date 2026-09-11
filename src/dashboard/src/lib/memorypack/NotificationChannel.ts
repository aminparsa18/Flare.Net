// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/NotificationChannelModels.cs`'s
// `NotificationChannel` field-for-field, in declared order. Can't carry
// `[GenerateTypeScript]` itself: it has its own `DateTimeOffset` `CreatedAt`/`UpdatedAt` -
// same reason `AlertRule.ts` is hand-written instead of generated.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { NotificationChannelType } from '$lib/generated/memorypack/NotificationChannelType.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class NotificationChannel {
	id: string;
	name: string | null;
	description: string | null;
	type: NotificationChannelType;
	webhookUrl: string | null;
	telegramBotToken: string | null;
	telegramChatId: string | null;
	emailTo: string | null;
	pagerDutyRoutingKey: string | null;
	createdAt: Date;
	updatedAt: Date;

	constructor() {
		this.id = '00000000-0000-0000-0000-000000000000';
		this.name = null;
		this.description = null;
		this.type = 0;
		this.webhookUrl = null;
		this.telegramBotToken = null;
		this.telegramChatId = null;
		this.emailTo = null;
		this.pagerDutyRoutingKey = null;
		this.createdAt = new Date(0);
		this.updatedAt = new Date(0);
	}

	static serialize(value: NotificationChannel | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: NotificationChannel | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(11);
		writer.writeGuid(value.id);
		writer.writeString(value.name);
		writer.writeString(value.description);
		writer.writeInt32(value.type);
		writer.writeString(value.webhookUrl);
		writer.writeString(value.telegramBotToken);
		writer.writeString(value.telegramChatId);
		writer.writeString(value.emailTo);
		writer.writeString(value.pagerDutyRoutingKey);
		writeDateTimeOffset(writer, value.createdAt);
		writeDateTimeOffset(writer, value.updatedAt);
	}

	static serializeArray(value: (NotificationChannel | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (NotificationChannel | null)[] | null): void {
		writer.writeArray(value, (writer, x) => NotificationChannel.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): NotificationChannel | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): NotificationChannel | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new NotificationChannel();
		if (count == 11) {
			value.id = reader.readGuid();
			value.name = reader.readString();
			value.description = reader.readString();
			value.type = reader.readInt32();
			value.webhookUrl = reader.readString();
			value.telegramBotToken = reader.readString();
			value.telegramChatId = reader.readString();
			value.emailTo = reader.readString();
			value.pagerDutyRoutingKey = reader.readString();
			value.createdAt = readDateTimeOffset(reader);
			value.updatedAt = readDateTimeOffset(reader);
		} else if (count > 11) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.id = reader.readGuid();
			if (count == 1) return value;
			value.name = reader.readString();
			if (count == 2) return value;
			value.description = reader.readString();
			if (count == 3) return value;
			value.type = reader.readInt32();
			if (count == 4) return value;
			value.webhookUrl = reader.readString();
			if (count == 5) return value;
			value.telegramBotToken = reader.readString();
			if (count == 6) return value;
			value.telegramChatId = reader.readString();
			if (count == 7) return value;
			value.emailTo = reader.readString();
			if (count == 8) return value;
			value.pagerDutyRoutingKey = reader.readString();
			if (count == 9) return value;
			value.createdAt = readDateTimeOffset(reader);
			if (count == 10) return value;
			value.updatedAt = readDateTimeOffset(reader);
			if (count == 11) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (NotificationChannel | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (NotificationChannel | null)[] | null {
		return reader.readArray((reader) => NotificationChannel.deserializeCore(reader));
	}
}
