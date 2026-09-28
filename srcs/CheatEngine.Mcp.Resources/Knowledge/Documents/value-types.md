# Value types and data encodings

How games store numbers, flags, pointers and text, and which type name each tool expects. Read it before choosing a
scan type or reading a field. The scan loop is in [value-scans](value-scans.md); a scan that finds nothing is covered
in [troubleshooting-scans](troubleshooting-scans.md).

## Predict before you scan

- Whole number on screen: `int32`, then `float` with `between` value±0.5, then `double`, `int16` or `int64`.
- Bar, gauge, timer or position: `float` with an unknown first scan (`double` for UE5 positions).
- Money from 2,147,483,648 to 4,294,967,295 may be a `uint32`: scan its signed `int32` twin (see Integers). An `int64`
  exact scan finds such a value only where the next 4 bytes are zero. Larger money is `int64` or `double`.
- Script engines, browsers and emulators store numbers their own way (doubles, tagged integers, big-endian): identify
  the engine first ([engine_triage](../Workflows/engine-triage.md)). A 4-byte scan on a double engine finds nothing.

## Three type vocabularies

| Data | Memory, structure, conversion `valueType` | `scan_first.valueType` | Record `variableType` | Bytes |
|---|---|---|---|---|
| 8-bit integer | `int8`, `uint8` | `byte` (unsigned, 0 to 255) | 0 | 1 |
| 16-bit integer | `int16`, `uint16` | `int16` (signed) | 1 | 2 |
| 32-bit integer | `int32`, `uint32` | `int32` (signed, the default) | 2 (the default) | 4 |
| 64-bit integer | `int64`, `uint64` | `int64` (signed) | 3 | 8 |
| IEEE float | `float` | `float` | 4 | 4 |
| IEEE double | `double` | `double` | 5 | 8 |
| Pointer | `pointer` | none: `int64` (x64) or `int32`, in decimal | 3 on x64, 2 on 32-bit (12 refused) | 4 or 8 |
| UTF-8 text (ASCII is the same) | `string` | `string`, exact only | 6 | n |
| UTF-16 text | `wstring` | `wstring`, exact only | 6 with the UTF-16 flag (7 refused) | 2n |
| Raw bytes | `bytes` | `bytes` (hex pairs), exact only | 8 with a length or a value | n |
| Bit range | none | none | 9 (no bit range settable) | bits |

- The first vocabulary serves `memory_read`, `memory_read_batch`, `memory_write`, `memory_write_batch`,
  `pointer_read_chain`, structure elements, `util_convert_value` and `aob_find_value` (which refuses `bytes`; use
  `aob_find`). `memory_read_samples` and `memory_compare_snapshot` take only the fixed-size types: `int8` to `uint64`,
  `float`, `double` and `pointer`. `structure_read` and `structure_get` can also report `binary`, `custom` or `other`
  for elements that CE itself created (dissect window, PDB, autoguess).
- `scan_first` also accepts CE's names (`integer8` to `integer64`, `int`, `long`, `singlefloat`, `doublefloat`,
  `utf8string`, `utf16string`, `bytearray`). There is no `auto` scan type: `auto` in a workflow prompt's arguments
  (find_known_value, find_unknown_value) means the assistant picks the type.
- Record codes also include 11 (Auto Assembler script), 13 (custom) and 14, the group record that `record_group`
  makes. 14 is not CE's own group header: per CE's source, a save stores it as type `Error`, and a reload makes it a
  Byte record at address 0, children intact. The tools set no binary bit range, custom type name or signed display;
  a byte array record always gets hex display. Full list: [cheat tables](cheat-tables.md#record-fields-and-types).
- `record_create` and `record_update` refuse 7, 10 and 12, whose value CE can neither read nor write: use 6 with the
  UTF-16 flag for text, the value's own type instead of 10, and 3 (x64) or 2 (32-bit) for a pointer-sized number, with
  offsets for a chain. `record_find` still matches such codes in records that a table or Lua made; their value is empty.
- A UTF-16 text record, 32 characters long, created without writing a value:
  `record_create(records=[{description="Name", address="...", variableType=6, unicode=true, length=32}])`.
  The length counts characters for 6 and bytes for 8 (1 to 4096); a string or byte array record created without a
  value needs one.
- The freeze_value prompt offers `int8` to `uint64`, `float`, `double` and `string`; build UTF-16 or byte array records
  with `record_create` itself.
- Call arguments use other codes: `mono_invoke_method` takes CE codes (0 byte or bool, 1 int16, 2 int32, 3 int64,
  4 float, 5 double, 6 string, 12 object or pointer); `exec_call_remote` and `exec_call_method` take `integer`,
  `float` or `double`, with every value as a string.

## Integers

- x86 and x64 are little-endian: the lowest byte comes first, so int32 100 is `64 00 00 00` in memory. Big-endian
  guests are covered in the last section.
- Two's complement: int32 -1 and uint32 4294967295 are the same bytes `FF FF FF FF`. An exact match compares bits;
  signedness changes only the display and ordering comparisons.
- Scan values are decimal; `0x` is refused. `byte` is unsigned: -1 is refused, so scan 255. `int16`, `int32` and
  `int64` are signed, and a value above the signed maximum is refused. Pass it as its signed twin:
  `util_convert_value(value="50000", sourceType="uint16", targetType="int16")` gives -15536, then
  `scan_first(valueType="int16", value="-15536")`. Money of 3,000,000,000 scans as `int32` -1294967296.
- `memory_write`, `memory_write_batch`, `aob_find_value` and `util_convert_value` take decimal or `0x` hex. Hex
  without a sign may fill the width (`0xFFFFFFFF` is -1 as `int32`); signed decimal must fit the signed range; unsigned
  types refuse negatives. Reads return decimal.
- Record values follow CE's own parser: a Byte record given -1 stores `FF`. Signed display is CE's ShowAsSigned
  option, which no tool sets.
- CE's scanner orders integers as unsigned for `greater`, `less`, `increased` and `decreased`, and `increasedBy` or
  `decreasedBy` wrap around (per its source, not checked on 7.7): 0 to -1 counts as increased, and less than 10 drops
  negatives. `between` compares signed when a bound is negative, so use it with a negative lower bound, or use
  `changed` ([value-scans](value-scans.md)). `memory_compare_snapshot` orders by the type's signedness, so pick `int32`
  or `uint32` on purpose.
- Width traps: a `byte` scan for 60 also hits the low byte of every wider 60; an `int32` scan misses a 2-byte field
  whose next bytes are not zero; an `int32` hit can be the low half of an `int64`.
- Declared widths on Windows: C and C++ `char` 1, `short` and `wchar_t` 2, `int` and `long` 4 (also on x64),
  `long long` 8, pointers 8 on x64. C# `byte`/`sbyte` 1, `short`/`ushort`/`char` 2, `int`/`uint` 4, `long`/`ulong` 8;
  C# `decimal` is 16 bytes and not an IEEE float. Enums are usually 4-byte ints.

## Floats and doubles

- `float` is IEEE binary32: 24-bit significand, about 7 significant digits, integers exact up to 16,777,216 (2^24).
  `double` is binary64: 53 bits, 15 to 16 digits, integers exact up to 2^53.
- Most decimals are not exact: float 0.1 is 0.100000001490116, and 25.256 is 25.2560005.

| Value | Float bits | Float bytes in memory | Double bits |
|---|---|---|---|
| 0.5 | `3F000000` | `00 00 00 3F` | `3FE0000000000000` |
| 1 / -1 | `3F800000` / `BF800000` | `00 00 80 3F` / `00 00 80 BF` | `3FF0000000000000` / `BFF0000000000000` |
| 2 | `40000000` | `00 00 00 40` | `4000000000000000` |
| 10 | `41200000` | `00 00 20 41` | `4024000000000000` |
| 100 | `42C80000` | `00 00 C8 42` | `4059000000000000` |
| 1000 | `447A0000` | `00 00 7A 44` | `408F400000000000` |
| 360 | `43B40000` | `00 00 B4 43` | `4076800000000000` |
| π | `40490FDB` | `DB 0F 49 40` | `400921FB54442D18` |

- Any other value: `util_convert_value(value="100", sourceType="float", targetType="uint32")` returns the bits as an
  integer (`value` 1120403456) and `bytes` as they sit in memory (`00 00 C8 42`).
- Recognising: a dword whose top byte is `3C` to `47` (`BC` to `C7` when negative) is a float of magnitude 0.0078 to
  131,071. An int read as a float is a tiny denormal (int 100 reads as 1.4E-43); float 100 read as `int32` is
  1120403456. A round double often has a zero low dword (100.0 is `00 00 00 00 00 00 59 40`).
- Rounding: games show rounded values (57.38 shows as 57). The scan tools write a float value with
  `scan_first.floatDecimals` decimals (default 6 for float, 12 for double), so an exact 57 misses 57.38. Scan the
  display range: `scan_first(valueType="float", comparison="between", value="56.5", upperValue="57.5")` (57 to 58 if
  the game truncates). Write decimals with a point, never a comma. Each decimal fewer widens an exact match tenfold.
  Main also applies CE's rounding setting (Rounded (extreme) on a fresh install), which `scan_get_status.settings`
  shows; named scanners always use CE's default Rounded mode ([find_float_value](../Workflows/find-float-value.md)).
- Scans refuse NaN and Infinity; `memory_write`, `aob_find_value` and `util_convert_value` accept `NaN`, `Infinity`
  and `-Infinity`.
- Doubles, not floats: UE5 world vectors and rotators, GameMaker numbers, Godot GDScript floats, JavaScript numbers
  that are not small integers, Lua 5.1 and LuaJIT numbers, and often money in idle games. A float scan finds nothing
  there.

## Booleans, flags and bit fields

- C++ `bool` is 1 byte (0 or 1); Win32 `BOOL` is 4 bytes, any non-zero is true; `BOOLEAN` is 1 byte; a C# `bool`
  field is 1 byte. Ruby 1.8/1.9 (RGSS) stores false 0, true 2, nil 4; Flash AVM2 stores false 5, true 13.
- A `byte` exact scan for 1 floods. Scan unknown, toggle in game, then narrow with `changed` and `unchanged`
  ([find_flag](../Workflows/find-flag.md)).
- Bit fields: MSVC fills them from the least significant bit up, in units of the declared type (4 bytes for `int` or
  `unsigned`). Read the unit as that unsigned type, toggle one thing, read again; the XOR is the mask:
  `util_calculate(expression="0x41 ^ 0x45")` gives 4. Set a bit with `|`, clear it with `& ~`, and write back only
  after consent; the game may rewrite the unit meanwhile.
- Code hints: `movzx r32, byte ptr [...]` or `cmp byte ptr [...],0` reads a bool or byte; `test [...],2^n` tests one
  flag bit ([code-analysis](code-analysis.md)).
- Record code 9 (binary) exists, but no record tool sets its start bit or bit count: keep a byte or 4-byte record.

## Pointers

- A pointer is 8 bytes on an x64 target and 4 on a 32-bit one. `pointer` follows `process_get_current.pointerSize`;
  `util_convert_value.pointerSize` is explicit (default 8).
- Recognising: user-mode x64 addresses are at most `7FFF'FFFFFFFF`, so the high dword is at most `7FFF`; two "ints"
  where the second is that small are often one pointer. x64 structs keep pointers on 8-byte offsets. Classify a value
  with `memory_get_address_info(addresses=["<value>"])`.
- Holders of an address: `pointer_find_references(target="<address>")`, or its exact bytes with
  `aob_find_value(valueType="pointer", value="<address>")`. The scan tools have no pointer type: scan `int64` (x64)
  or `int32` exact with the address in decimal, which `util_calculate(expression="0x<address>")` gives as
  `util_calculate.unsignedValue`.
- The JVM and V8 can compress references to 4 bytes ([game-engines](game-engines.md)). Chains: [pointers](pointers.md).

## Scaled, fixed-point and encoded values

- Scaled: money ×100 (12.50 stored as 1250) or ×10; percentages as a 0-to-1 float or 0 to 100; 16.16 fixed point as
  an `int32` of value×65536; derived or inverse values (speed = constant ÷ stored).
- A guessed scale gives an exact scan of value×scale. An unknown scale needs an unknown first scan, then `increased`
  or `decreased` after each change. CE's percent scan is not in `scan_next`; the user can run it in CE's main scanner,
  and `scan_list_results` then reads its list.
- Encoded: XOR with a key, an added offset, or a second copy with a checksum. Encoding scrambles order, so use an
  unknown scan with `changed` and `unchanged`. The writer's registers often hold the plain value
  ([debugger](debugger.md)).
- Stop at protection: "obscured" or encrypted types from anti-cheat or anti-tamper libraries, DRM and licence checks
  mean the developer opposes modification. Tell the user; do not decode them ([safety](safety.md)).

## Tagged and engine-specific numbers

| Runtime | Storage | Scan | Write |
|---|---|---|---|
| RPG Maker XP, VX, VX Ace (Ruby RGSS) | Fixnum `2n+1` in 4 bytes | `int32` exact 2n+1 | 2m+1 |
| V8, compressed or 32-bit (Electron 14+) | Smi `2n` in 4 bytes; others boxed doubles | `double`, then `int32` 2n | 2m |
| V8, 64-bit uncompressed (older builds) | the integer in the upper half of 8 bytes | `int32` exact n (at slot+4) | n |
| Flash AVM2 | int atom `8n+6`; Number is a boxed double | `int32` 8n+6, or between 8n and 8n+7 | 8m+6 |
| GameMaker | 16-byte RValue: double at +0, kind at +0xC (0 = real) | `double`; check kind 0 | double |
| Godot GDScript | Variant: type at +0 (2 int, 3 float), payload at +8 | `int64` or `double` | same type |
| Unreal Engine | UE5 vectors, rotators, transforms are doubles; UE4 floats | `double` for UE5 positions | same type |
| Unity, native C++ | plain fields of the declared type | offsets from `mono_list_fields` | same type |

- Verify a Godot hit with `memory_read(address="<hit>-8", valueType="int32")`: 2 or 3.
- Unity (Mono or IL2CPP), with the collector attached after consent: `mono_get_object(address="<hit>")` walks back
  from a hit to its object and returns the class, `mono_get_object.offsetInObject` and the fields with their values
  ([mono-and-dotnet](mono-and-dotnet.md)).
- RPG Maker MV and MZ run on NW.js, whose V8 build picks the row: attach to the renderer process with the most memory
  and try `double` first (a forum report says their numbers are mostly doubles; UNVERIFIED as a rule). Flash typed
  slots may also hold a plain `int32` or `double` (UNVERIFIED).
- Moving garbage collectors (V8, the JVM, CoreCLR) relocate objects: re-find a value rather than pointer-scan it.
  Unity's Boehm collector (Mono and IL2CPP) does not move objects, but they die with their scene. In interpreters,
  "find what writes" lands in shared VM code that writes every variable: prefer data edits
  ([game-engines](game-engines.md), [unreal-engine](unreal-engine.md), [mono-and-dotnet](mono-and-dotnet.md)).

## Strings

- `string` is UTF-8 (ASCII text is identical); `wstring` is UTF-16LE, used by Windows APIs, .NET, Unity and Unreal
  `FString`. Other code pages (Shift-JIS, custom font tables) match neither: search bytes, or rename in game and scan
  unknown with `changed`.
- Find text: `aob_find_value(valueType="wstring", value="Hero", writable="required")` matches the exact UTF-16 bytes
  at any address (case-sensitive, no terminator). The scan tools allow only `exact` for text. Main keeps CE's Case
  sensitive and Codepage boxes as the user left them, and `scan_get_status.settings` shows both. Per CE's source, a
  main `string` scan with Codepage ticked uses the code page instead of UTF-8, which differs only for non-ASCII text.
- Read with `memory_read(address="...", valueType="wstring", length=64)`. CE's `readString` source counts
  `memory_read.length` in bytes, so this returns at most about 32 UTF-16 characters; raise it (up to 4096) for long
  text.

| Container (x64) | Layout | Text |
|---|---|---|
| `char[N]`, `wchar_t[N]` | inline, zero-terminated, then leftover bytes | inline |
| `char*`, `wchar_t*` | 8-byte pointer | follow it |
| MSVC `std::string` | 32 bytes: +0 buffer or pointer, +0x10 size, +0x18 capacity | inline if capacity ≤ 15 (wide ≤ 7) |
| libstdc++ `std::string` | 32 bytes: +0 pointer, +8 size, +0x10 capacity or buffer | pointer targets +0x10 when short |
| libc++ `std::string` | 24 bytes | up to 22 characters inline |
| CoreCLR `System.String` | +0 MethodTable, +0x8 int32 length, +0xC characters | inline UTF-16 |
| Mono or IL2CPP `System.String` | +0 class, +0x8 monitor, +0x10 int32 length, +0x14 characters | inline UTF-16 |
| Unreal `FString` | +0 Data pointer, +8 int32 Num (includes the terminator), +0xC int32 Max | UTF-16 at Data |
| Unreal `FName` | int32 ComparisonIndex, int32 Number | a name-table index, not text |

- From a UTF-16 hit to its object (x64): Mono or IL2CPP object = hit-0x14 (length at hit-4); CoreCLR object =
  hit-0xC. Then `pointer_find_references(target="<object>")` finds the field that holds it; on Unity,
  `mono_get_object(address="<reference address>")` names the holding class, and its `mono_get_object.offsetInObject`
  the field ([find_text](../Workflows/find-text.md)).
- Editing, after consent: same length or shorter,
  `memory_write(address="...", valueType="wstring", value="...", nullTerminate=true)`, then update the length field
  (managed length, `std::string` size, `FString` Num). Never write past the old length: the next object follows.
  Managed strings are immutable and may be shared by every use of a literal; the game may keep other copies or rebuild
  the text each frame.

## Big-endian data in emulators

- Big-endian guests: GameCube and Wii (Dolphin), Wii U (Cemu), PS3 (RPCS3), Xbox 360. Little-endian: PS1, PS2
  (PCSX2), PSP (PPSSPP), GBA, DS, Switch.
- Read and write with a byte order: `memory_read(address="...", valueType="int32", byteOrder="big_endian")` reads
  `00 00 00 64` as 100, and
  `memory_write(address="...", valueType="int32", value="999", byteOrder="big_endian", verify=true)` writes
  `00 00 03 E7` (after consent). The items of `memory_read_batch` and `memory_write_batch`, and `memory_read_samples`,
  take the same `byteOrder`. It applies to `int16` to `uint64`, `float` and `double`; `int8`, `uint8`, `pointer`,
  `string`, `wstring` and `bytes` refuse `big_endian` (invalid_argument; a batch refuses the whole batch). A guest
  pointer is a guest address: read it as `uint32` with `big_endian` (decimal), then map it to a host address
  ([emulators](emulators.md)).
- Exact value: scans, `aob_find_value`, `memory_compare_snapshot` and `pointer_read_chain` have no byte order. Encode
  the bytes with `util_convert_value(value="100", sourceType="int32", targetType="bytes", byteOrder="big_endian")`,
  which gives `00 00 00 64` (float 100 gives `42 C8 00 00`), then search them:
  `scan_first(scannerName="emu", valueType="bytes", value="00 00 00 64", includeMapped=true)` or
  `aob_find(patterns=["00 00 00 64"], writable="required", includeMapped=true)`.
- Unknown value: `changed` and `unchanged` compare bits, so an `int32` or `int16` unknown scan of the value's width
  also narrows big-endian data. `increased` and `decreased` need CE's "2 Byte Big Endian", "4 Byte Big Endian" or
  "Float Big Endian" custom types (Settings → Extra Custom Types; no 8-byte or double one ships), which the scan tools
  cannot select: ask the user to scan in CE, then read main's list with `scan_list_results` (`scan_next` on main
  refuses a custom type). `memory_compare_snapshot` also orders little-endian; to see which candidates follow the
  value, sample them with `memory_read_samples(addresses=["..."], valueType="int32", byteOrder="big_endian")`.
- `util_convert_value.byteOrder` only changes how `bytes` are shown: a numeric `targetType` is always decoded
  little-endian, so it never decodes big-endian input. Decode memory with `memory_read` and its byte order; decode
  bytes you already hold by reversing the pairs, then
  `util_convert_value(value="64 00 00 00", sourceType="bytes", targetType="int32")` gives 100.
- Guest RAM usually sits in mapped memory (Cemu 2.x uses private memory), which CE's scanner skips by default.
  `scan_first` on a named scanner, `aob_find` and `aob_find_value` include it with `includeMapped` (not with a
  module); main needs the user to tick MEM_MAPPED in CE's Scan Settings. The override is CE-wide, and the call ends
  every scan-region override afterwards, including one a table or `lua_execute` set. Guest pointers are guest
  addresses (`80xxxxxx` on GameCube), not host ones: [emulators](emulators.md),
  [emulator_memory](../Workflows/emulator-memory.md).

Writes, freezes and string edits change the target: only on single-player or offline software the user owns or may
modify, never on online, competitive or anti-cheat-protected games, and only after explaining each change. Records:
[cheat-tables](cheat-tables.md); layouts: [structures](structures.md).

## Sources

- CE source (github.com/cheat-engine/cheat-engine): `memscan.pas` (unsigned ordering, signed between, wrapping
  increased by), `MemoryRecordUnit.pas` (`setVarType`, `GetValue`, `getByteSize`), `CEFuncProc.pas` (type 14 saved as
  `Error`), `MainUnit.pas` (Codepage), `LuaHandler.pas` (`readStringEx`), `autorun/bigendian.lua`; local CE 7.7
  `celua.txt` and `defines.lua`.
- https://wiki.cheatengine.org/index.php?title=Help_File%3AValue_types
- https://learn.microsoft.com/cpp/build/ieee-floating-point-representation
- https://learn.microsoft.com/cpp/cpp/data-type-ranges ; https://learn.microsoft.com/cpp/cpp/cpp-bit-fields
- https://learn.microsoft.com/windows/win32/learnwin32/windows-coding-conventions
- https://learn.microsoft.com/cpp/build/x64-software-conventions
- https://github.com/microsoft/STL (`stl/inc/xstring`)
- https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/object.h
- https://github.com/mono/mono/blob/main/mono/metadata/object-internals.h
- https://github.com/Fischsalat/UnrealContainers (`FString` Num includes the terminator)
- https://v8.dev/blog/pointer-compression ; https://github.com/adobe/avmplus (`core/atom.h`)
- https://github.com/ruby/ruby/blob/v1_8_7/ruby.h (Fixnum, false, true, nil)
- https://docs.godotengine.org/en/stable/engine_details/architecture/variant_class.html
- https://github.com/AurieFramework/YYToolkit
- https://dev.epicgames.com/documentation/unreal-engine/large-world-coordinates-in-unreal-engine-5
- https://www.lua.org/manual/5.1/manual.html#2.2
