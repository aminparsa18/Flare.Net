// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MaintenanceWindowModels.cs`'s
// `MaintenanceWindowRequest` field-for-field, in declared order - hand-written for the same
// `DateTimeOffset`/`IReadOnlyList<T>` reasons as `MaintenanceWindow.ts`. Serialize-only: the
// dashboard never decodes a request.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { writeDateTimeOffset, writeNullableDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class MaintenanceWindowRequest {
	name: string | null = null;
	description: string | null = null;
	ruleIds: string[] | null = null;
	startsAt: Date = new Date(0);
	endsAt: Date = new Date(0);
	recurrence: number | null = null;
	daysOfWeek: number[] | null = null;
	repeatUntil: Date | null = null;
	timeZone: string | null = null;

	static serialize(value: MaintenanceWindowRequest): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		writer.writeObjectHeader(9);
		writer.writeString(value.name);
		writer.writeString(value.description);
		writer.writeArray(value.ruleIds, (writer, x) => writer.writeGuid(x));
		writeDateTimeOffset(writer, value.startsAt);
		writeDateTimeOffset(writer, value.endsAt);
		writer.writeNullableInt32(value.recurrence);
		writer.writeArray(value.daysOfWeek, (writer, x) => writer.writeInt32(x));
		writeNullableDateTimeOffset(writer, value.repeatUntil);
		writer.writeString(value.timeZone);
		return writer.toArray();
	}
}
