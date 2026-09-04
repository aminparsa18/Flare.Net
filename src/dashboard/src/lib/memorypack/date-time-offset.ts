// Hand-written companion to MemoryPack's generated TypeScript runtime
// ($lib/generated/memorypack/MemoryPackWriter.ts / MemoryPackReader.ts) - this file is NOT
// itself generated. MemoryPack's C# side supports `DateTimeOffset` as a built-in type (no
// custom formatter needed - see MemoryPack's README "Built-in supported types"), but its
// TypeScript generator's member-type mapping only covers
// bool/numeric-primitives/bigint/string/Guid/enum/DateTime (see MemoryPackGenerator's
// `ConvertFromSymbol` in MemoryPack.Generator/TypeScriptMember.cs) - `DateTimeOffset` isn't
// one of them, so any Flare.Api DTO with a `DateTimeOffset` member throws MEMPACK031 the
// moment it's annotated `[GenerateTypeScript]`. Every hand-written class under
// `$lib/memorypack/` that mirrors such a DTO calls the functions below at the right
// position instead of a generated `write`/`readDateTimeOffset` method.
//
// Wire format verified empirically against the real MemoryPack 1.21.4 package (a throwaway
// `dotnet run` calling `MemoryPackSerializer.Serialize` on known `DateTimeOffset` values and
// dumping the bytes - see docs-internal/investigations/memorypack-serialization-migration-scope.md's
// Phase 2 section for the exact hex), not assumed from documentation - MemoryPack doesn't
// publish this layout. 16 bytes total, matching .NET's own `DateTimeOffset` internal shape:
//   [0..8)  offsetMinutes, signed 64-bit little-endian
//   [8..16) the wall-clock "local" tick value *before* subtracting the offset, with the top
//           2 (DateTimeKind) bits masked off - the exact same `dateTimeMask`/`unixEpochTicks`
//           trick MemoryPack's own generated `writeDate`/`readDate` already use to reverse
//           .NET `DateTime`'s tick+Kind packing for TypeScript. `DateTimeOffset` simply isn't
//           a type MemoryPack's generator has that trick implemented for (yet) - this file
//           extends the same, already-proven approach to it.
// A nullable `DateTimeOffset?` prepends an 8-byte int64 "has value" flag (0 or 1), exactly
// like MemoryPack's generated `writeNullableInt64`/`writeNullableDate` do for every other
// unmanaged type - 24 bytes total.
//
// Every `DateTimeOffset` in `Flare.Api/Model` already originates as UTC (ClickHouse-stored
// timestamps, `DateTimeOffset.UtcNow`) - `offsetMinutes` is always 0 in practice, so a plain
// JS `Date` (inherently a UTC instant) round-trips losslessly through these functions with
// nothing dropped.

import type { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import type { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

const unixEpochTicks = 621355968000000000n;
const dateTimeMask = 0b00111111_11111111_11111111_11111111_11111111_11111111_11111111_11111111n;
const ticksPerMinute = 600000000n;

/** Writes a `DateTimeOffset`. `offsetMinutes` defaults to 0 (UTC) - see this file's header comment. */
export function writeDateTimeOffset(writer: MemoryPackWriter, value: Date, offsetMinutes = 0n): void {
	const unixMillisecond = BigInt(value.getTime());
	const utcTicks = unixMillisecond * 10000n + unixEpochTicks;
	const localTicks = utcTicks + offsetMinutes * ticksPerMinute;
	writer.writeInt64(offsetMinutes);
	writer.writeUint64(localTicks & dateTimeMask);
}

/** Reads a `DateTimeOffset`, returning the UTC instant. The offset itself is discarded (see this file's header comment). */
export function readDateTimeOffset(reader: MemoryPackReader): Date {
	const offsetMinutes = reader.readInt64();
	const localTicks = reader.readUint64() & dateTimeMask;
	const utcTicks = localTicks - offsetMinutes * ticksPerMinute;
	const unixMillisecond = (utcTicks - unixEpochTicks) / 10000n;
	return new Date(Number(unixMillisecond));
}

/** Writes a `DateTimeOffset?`. Always writes the full 24 bytes (matching MemoryPack's own `writeNullableInt64`/`writeNullableDate` pattern), even when `value` is `null`. */
export function writeNullableDateTimeOffset(writer: MemoryPackWriter, value: Date | null): void {
	if (value == null) {
		writer.writeInt64(0n);
		writeDateTimeOffset(writer, new Date(0));
		return;
	}
	writer.writeInt64(1n);
	writeDateTimeOffset(writer, value);
}

/** Reads a `DateTimeOffset?`. Always consumes the full 24 bytes regardless of the result, matching `writeNullableDateTimeOffset`. */
export function readNullableDateTimeOffset(reader: MemoryPackReader): Date | null {
	const hasValue = reader.readInt64();
	const value = readDateTimeOffset(reader);
	return hasValue === 0n ? null : value;
}
