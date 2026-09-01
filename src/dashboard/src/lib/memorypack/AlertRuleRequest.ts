// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/AlertModels.cs`'s `AlertRuleRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `LogFilter` (blocked - see `$lib/memorypack/LogFilter.ts`'s header comment).
// `Threshold` (`AlertThreshold`) has no such problem, so it's a real generated class,
// reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { AlertThreshold } from '$lib/generated/memorypack/AlertThreshold.js';
import { LogFilter } from '$lib/memorypack/LogFilter';

export class AlertRuleRequest {
	name: string | null;
	description: string | null;
	enabled: boolean | null;
	condition: LogFilter | null;
	threshold: AlertThreshold | null;
	windowSeconds: number;
	cooldownSeconds: number | null;
	webhookUrl: string | null;
	telegramBotToken: string | null;
	telegramChatId: string | null;
	emailTo: string | null;

	constructor() {
		this.name = null;
		this.description = null;
		this.enabled = null;
		this.condition = null;
		this.threshold = null;
		this.windowSeconds = 0;
		this.cooldownSeconds = null;
		this.webhookUrl = null;
		this.telegramBotToken = null;
		this.telegramChatId = null;
		this.emailTo = null;
	}

	static serialize(value: AlertRuleRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: AlertRuleRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(11);
		writer.writeString(value.name);
		writer.writeString(value.description);
		writer.writeNullableBoolean(value.enabled);
		LogFilter.serializeCore(writer, value.condition);
		AlertThreshold.serializeCore(writer, value.threshold);
		writer.writeInt32(value.windowSeconds);
		writer.writeNullableInt32(value.cooldownSeconds);
		writer.writeString(value.webhookUrl);
		writer.writeString(value.telegramBotToken);
		writer.writeString(value.telegramChatId);
		writer.writeString(value.emailTo);
	}

	static deserialize(buffer: ArrayBuffer): AlertRuleRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): AlertRuleRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new AlertRuleRequest();
		if (count == 11) {
			value.name = reader.readString();
			value.description = reader.readString();
			value.enabled = reader.readNullableBoolean();
			value.condition = LogFilter.deserializeCore(reader);
			value.threshold = AlertThreshold.deserializeCore(reader);
			value.windowSeconds = reader.readInt32();
			value.cooldownSeconds = reader.readNullableInt32();
			value.webhookUrl = reader.readString();
			value.telegramBotToken = reader.readString();
			value.telegramChatId = reader.readString();
			value.emailTo = reader.readString();
		} else if (count > 11) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.name = reader.readString();
			if (count == 1) return value;
			value.description = reader.readString();
			if (count == 2) return value;
			value.enabled = reader.readNullableBoolean();
			if (count == 3) return value;
			value.condition = LogFilter.deserializeCore(reader);
			if (count == 4) return value;
			value.threshold = AlertThreshold.deserializeCore(reader);
			if (count == 5) return value;
			value.windowSeconds = reader.readInt32();
			if (count == 6) return value;
			value.cooldownSeconds = reader.readNullableInt32();
			if (count == 7) return value;
			value.webhookUrl = reader.readString();
			if (count == 8) return value;
			value.telegramBotToken = reader.readString();
			if (count == 9) return value;
			value.telegramChatId = reader.readString();
			if (count == 10) return value;
			value.emailTo = reader.readString();
			if (count == 11) return value;
		}
		return value;
	}
}
