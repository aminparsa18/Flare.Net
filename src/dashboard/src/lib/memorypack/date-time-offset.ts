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
// (Upstream fix proposed: https://github.com/Cysharp/MemoryPack/pull/459 - not merged as
// of this writing. If/when it lands, DTOs with a `DateTimeOffset` member can drop the
// hand-written wrapper and use `[GenerateTypeScript]` directly like every other DTO.)
//
// Wire format verified empirically against the real MemoryPack 1.21.4 package (a throwaway
// `dotnet run` calling `MemoryPackSerializer.Serialize` on known `DateTimeOffset` values -
// zero/positive/negative offsets, `MinValue`/`MaxValue`, sub-millisecond ticks - and dumping
// the bytes), not assumed from documentation - MemoryPack doesn't publish this layout, and
// it's a raw struct blit (`UnmanagedFormatter<DateTimeOffset>`, not a hand-crafted
// formatter), so the layout is CLR-implementation-defined, not a stable public contract.
// 16 bytes total:
//   [0..4)   offsetMinutes, signed 32-bit little-endian
//   [4..8)   padding (struct alignment - always zero, written but otherwise unused)
//   [8..16)  UTC ticks directly (NOT locally-adjusted ticks - the offset above is metadata
//            only, not part of the instant calculation), with the top 2 (DateTimeKind)
//            bits masked off - the same `dateTimeMask`/`unixEpochTicks` trick MemoryPack's
//            own generated `writeDate`/`readDate` already use for `DateTime`.
// A nullable `DateTimeOffset?` prepends an 8-byte int64 "has value" flag (0 or 1), exactly
// like MemoryPack's generated `writeNullableInt64`/`writeNullableDate` do for every other
// unmanaged type - 24 bytes total.
//
// An earlier version of this file assumed a 64-bit offset field and *locally-adjusted*
// ticks (requiring `+`/`- offsetMinutes * ticksPerMinute` to convert), which happened to
// round-trip correctly only because every `DateTimeOffset` in `Flare.Api/Model` already
// originates as UTC (`offsetMinutes` always 0 in practice - ClickHouse-stored timestamps,
// `DateTimeOffset.UtcNow`) - with a zero offset that wrong assumption's arithmetic reduces
// to a no-op, masking the bug. It was caught and fixed while preparing the PR above (whose
// own `MemoryLayoutTest` pins this exact layout against a live `MemoryPackSerializer` call
// as a permanent regression guard) - see
// docs-internal/investigations/memorypack-vs-json-benchmark.md's Finding 4 follow-up.
// Because ticks are always UTC on the wire regardless of `offsetMinutes`, no tick
// adjustment math is needed at all now (not even a fast-pathed one) - `offsetMinutes` is
// carried through only as data, never used to compute the instant.

import type { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import type { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

const unixEpochTicks = 621355968000000000n;
const dateTimeMask = 0b00111111_11111111_11111111_11111111_11111111_11111111_11111111_11111111n;

/** Writes a `DateTimeOffset`. `offsetMinutes` defaults to 0 (UTC) - see this file's header comment; it's written as-is and never affects the encoded instant. */
export function writeDateTimeOffset(writer: MemoryPackWriter, value: Date, offsetMinutes = 0): void {
	const unixMillisecond = BigInt(value.getTime());
	const utcTicks = unixMillisecond * 10000n + unixEpochTicks;
	writer.writeInt32(offsetMinutes);
	writer.writeInt32(0); // struct padding - always zero
	writer.writeUint64(utcTicks & dateTimeMask);
}

/** Reads a `DateTimeOffset`, returning the UTC instant. The offset itself is discarded (see this file's header comment - it never affected the instant on the wire either). */
export function readDateTimeOffset(reader: MemoryPackReader): Date {
	reader.readInt32(); // offsetMinutes - metadata only, not part of the instant
	reader.readInt32(); // struct padding
	const utcTicks = reader.readUint64() & dateTimeMask;
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
