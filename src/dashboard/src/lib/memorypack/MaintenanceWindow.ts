// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MaintenanceWindowModels.cs`'s `MaintenanceWindow`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`: it has
// `DateTimeOffset` and `IReadOnlyList<T>` members - same reasons `NotificationChannel.ts` and
// `AlertRule.ts` are hand-written. `recurrence` is the raw `MaintenanceWindowRecurrence`
// ordinal and `daysOfWeek` raw `System.DayOfWeek` ordinals (0 = Sunday). Decode-only.

import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, readNullableDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class MaintenanceWindow {
	id: string;
	name: string | null;
	description: string | null;
	ruleIds: (string | null)[] | null;
	startsAt: Date;
	endsAt: Date;
	recurrence: number;
	daysOfWeek: number[] | null;
	repeatUntil: Date | null;
	timeZone: string | null;
	createdAt: Date;
	updatedAt: Date;

	constructor() {
		this.id = '00000000-0000-0000-0000-000000000000';
		this.name = null;
		this.description = null;
		this.ruleIds = null;
		this.startsAt = new Date(0);
		this.endsAt = new Date(0);
		this.recurrence = 0;
		this.daysOfWeek = null;
		this.repeatUntil = null;
		this.timeZone = null;
		this.createdAt = new Date(0);
		this.updatedAt = new Date(0);
	}

	static deserialize(buffer: ArrayBuffer): MaintenanceWindow | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MaintenanceWindow | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MaintenanceWindow();
		if (count == 12) {
			value.id = reader.readGuid();
			value.name = reader.readString();
			value.description = reader.readString();
			value.ruleIds = reader.readArray((reader) => reader.readGuid());
			value.startsAt = readDateTimeOffset(reader);
			value.endsAt = readDateTimeOffset(reader);
			value.recurrence = reader.readInt32();
			value.daysOfWeek = reader.readArray((reader) => reader.readInt32());
			value.repeatUntil = readNullableDateTimeOffset(reader);
			value.timeZone = reader.readString();
			value.createdAt = readDateTimeOffset(reader);
			value.updatedAt = readDateTimeOffset(reader);
		} else {
			// No older/newer shape exists yet - a new trailing field appends an `else if` branch
			// here, same versioning scheme every other hand-written class in this folder uses.
			throw new Error(`MaintenanceWindow: unexpected property count ${count}.`);
		}
		return value;
	}
}
