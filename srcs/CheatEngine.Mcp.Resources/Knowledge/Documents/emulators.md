# Emulator memory

An emulator keeps each guest RAM area (GameCube MEM1, PS2 EE RAM, ...) in a block of host memory, often a mapped view
that Cheat Engine's scanner skips unless told otherwise, and several consoles store numbers big-endian. The memory
tools read and write big-endian numbers directly, and named scans and byte-pattern searches can include mapped memory
for one call. Read this page before you scan an emulated game; the [emulator_memory](../Workflows/emulator-memory.md)
workflow follows the same steps.

Use this only on games the user owns or may modify, played offline, never in netplay or any online mode. Explain each
change and get consent before you write memory, change Cheat Engine's settings or run Lua ([safety](safety.md)).

## Guest byte order

| Byte order | Consoles (emulator) |
| --- | --- |
| Big-endian | GameCube and Wii (Dolphin), Wii U (Cemu), PS3 (RPCS3), N64, Xbox 360, 68000-based consoles |
| Little-endian | PS1, PS2 (PCSX2), PSP (PPSSPP), GBA, DS, Switch |

This is the guest CPU's order. An emulator may store RAM another way (N64 emulators keep 32-bit words in host order,
see Per-emulator notes), so confirm with a value you know before you rely on either. Reads and writes take the order
as a parameter; scans need byte-swapped values (Big-endian values below,
[value types](value-types.md#big-endian-data-in-emulators)).

## 1. Find the guest RAM base

1. Without an export, list regions and look for one sized like the console's RAM view (Dolphin MEM1: 33554432
   bytes, 0x2000000): `memory_list_regions(type="mapped", format="detailed", limit=2000)`, or for Cemu, whose memory is
   private, `memory_list_regions(type="private", format="detailed", limit=2000)`. Page with
   memory_list_regions.nextOffset. Several regions can match: step 3 decides.
2. PCSX2 exports the base: `module_list(nameContains="pcsx2")` gives the exe name, then
   `module_list_exports(module="<exe name>", nameContains="EEmem")` gives the address of EEmem, a pointer-sized
   variable that holds the base: `memory_read(address="<EEmem address>", valueType="pointer")`. PPSSPP: the user
   copies the base (Per-emulator notes).
3. Check the candidate with bytes you know. Dolphin: `memory_read(address="<base>+1C", valueType="bytes", size=4)`
   reads `C2 33 9F 3D` for a GameCube disc, and `memory_read(address="<base>", valueType="string", length=6)` reads
   the game ID. Elsewhere, find a value you can see in game at the offset you expect.
4. Name it for this session: `symbol_register(name="emuBase", address="<base>")`, after which `emuBase+3A1240`
   resolves in every tool. For PCSX2, `symbol_register(name="emuBase", address="[<EEmem address>]")` reads the pointer
   once, when you register it.

## 2. Make the scanner see mapped memory

Cheat Engine skips MEM_MAPPED regions by default: the Scan Settings checkbox whose caption ends "(E.g:File mapping,
emulator memory, slow)" is unticked on a fresh install. In its source this is one global setting that filters every
scan it runs: main and named scans and the byte-pattern searches of `aob_find` and `aob_find_value`, even inside an
explicit start and end address. Reads, writes and snapshots are not filtered. Cemu's private memory needs no change.

- Named scanner, the usual path:
  `scan_first(scannerName="emu", valueType="int32", value="100", startAddress="emuBase", endAddress="emuBase+2000000", includeMapped=true)`.
  scan_next keeps the regions of that first scan and needs no flag.
- Byte patterns: aob_find.includeMapped and aob_find_value.includeMapped do the same for one call. Scope them with
  startAddress and endAddress: with module they are refused (invalid_argument), as a module image is never mapped.
- The main scanner refuses scan_first.includeMapped (invalid_argument). For main, ask the user to tick MEM_MAPPED under
  Edit → Settings → Scan Settings; `scan_get_status(scannerName="main")` then reports settings.memMapped true. The
  ticked setting also applies to named scans and the byte-pattern searches, which then need no flag.
- The flag turns on Cheat Engine's MEM_MAPPED override (setSpecialScanOptionsOverride), which is Cheat Engine-wide,
  for that call only, then ends every scan-region override, including one a table or `lua_execute` set. It replaces
  the Lua override, so do not set that yourself. A scan_first that starts while another first scan runs with the other
  scan_first.includeMapped choice is refused as busy (retryable): repeat it once the running scan returns.
- If the override cannot be removed, the call reports hostEffect `cleanup_unconfirmed` (scan_first: partial_effect,
  whose results are kept when scanCompleted is true; the byte-pattern searches: host_refused). Cheat Engine may still
  scan mapped memory: run another call with the flag, which ends it, or ask the user to restart Cheat Engine. A Cheat
  Engine without the override refuses the flag as unsupported ([errors](errors-and-recovery.md)).
- Test once before you trust "no match": search bytes you just read, on Dolphin
  `aob_find(patterns=["C2 33 9F 3D"], startAddress="emuBase", endAddress="emuBase+FF", limit=2, includeMapped=true)`.
  No match while memory_read shows the bytes means the region is still skipped: compare snapshots instead.
- Snapshots need no setting: `memory_create_snapshot(name="ram-a", address="emuBase", size=16777216)`, one change in
  game, a second snapshot `ram-b`, then
  `memory_compare_snapshot(name="ram-a", compareTo="ram-b", valueType="int32", change="changed")`. A snapshot holds at
  most 16 MiB, and at most 16 snapshots and 64 MiB exist together: split larger RAM, and free them with
  `memory_delete_snapshot(name="ram-a")`. On a big-endian guest keep to `changed` and `unchanged`; `increased` and
  `decreased` compare little-endian numbers
  ([troubleshooting scans](troubleshooting-scans.md#emulators-and-mapped-memory)).

## 3. Search values

Scope every search to the guest RAM. scan_first.endAddress is exclusive; aob_find.endAddress is the last allowed match
address. The examples use PCSX2's EE RAM (32 MiB) and Dolphin's MEM1 (24 MiB), both mapped; Cemu's private RAM needs
no includeMapped. PPSSPP game RAM runs from `emuBase+8800000` up to `emuBase+A000000` (standard 32 MiB RAM).

Little-endian guests search as on a PC game:
`aob_find_value(valueType="int32", value="100", startAddress="emuBase", endAddress="emuBase+1FFFFFF", includeMapped=true)`
or
`scan_first(scannerName="emu", valueType="int32", value="100", startAddress="emuBase", endAddress="emuBase+2000000", includeMapped=true)`,
then scan_next.

### Big-endian values

- Read: `memory_read(address="emuBase+3A1240", valueType="int32", byteOrder="big_endian")` reads `00 00 03 E7` as
  999. The same byteOrder exists on `memory_write`, `memory_read_samples` and on the items of `memory_read_batch` and
  `memory_write_batch`:
  `memory_read_batch(items=[{address="emuBase+3A1240", valueType="int32", byteOrder="big_endian"}, {address="emuBase+3A1244", valueType="float", byteOrder="big_endian"}])`.
  It applies to int16 to uint64, float and double. A 1-byte value needs none. `pointer`, `string`, `wstring` and
  `bytes` refuse big_endian (invalid_argument, and a batch refuses the whole batch): read a guest pointer as bytes
  (Guest and host addresses).
- Write, after consent:
  `memory_write(address="emuBase+3A1240", valueType="int32", value="999", byteOrder="big_endian", verify=true)` writes
  `00 00 03 E7`. memory_write.previous holds the raw bytes: undo with
  `memory_write(address="emuBase+3A1240", valueType="bytes", value="<previous>")`.
- Exact: scans, `aob_find_value`, `memory_compare_snapshot` and `pointer_read_chain` have no byte order, so search the
  bytes. `util_convert_value(value="100", sourceType="int32", targetType="bytes", byteOrder="big_endian")` returns
  `00 00 00 64` in util_convert_value.bytes (float 100 gives `42 C8 00 00`). Search it with
  `aob_find(patterns=["00 00 00 64"], startAddress="emuBase", endAddress="emuBase+17FFFFF", alignment=4, limit=1000, includeMapped=true)`,
  or keep a session to narrow:
  `scan_first(scannerName="emu", valueType="bytes", value="00 00 00 64", startAddress="emuBase", endAddress="emuBase+1800000", alignment=4, includeMapped=true)`,
  then `scan_next(scannerName="emu", comparison="exact", value="00 00 00 5F")` once the value is 95.
- aob_find_value encodes little-endian only; give it the swapped number. Big-endian int32 100 is uint32 0x64000000:
  `aob_find_value(valueType="uint32", value="0x64000000", startAddress=..., endAddress=..., includeMapped=true)`.
- Unknown:
  `scan_first(scannerName="emu", valueType="int32", comparison="unknown", startAddress=..., endAddress=..., alignment=4, includeMapped=true)`,
  then `scan_next(scannerName="emu", comparison="changed")` or `scan_next(scannerName="emu", comparison="unchanged")`.
  These compare bits and work in either byte order; `increased` and `decreased` compare little-endian numbers and
  mislead. For an exact step, swap the value:
  `util_convert_value(value="00 00 00 64", sourceType="bytes", targetType="int32")` gives `1677721600`, then
  `scan_next(scannerName="emu", comparison="exact", value="1677721600")`.
- Increased or decreased: once changed and unchanged have narrowed the list, watch up to 64 candidates from
  `scan_list_results(scannerName="emu", maximumResults=64)` while the value moves in game:
  `memory_read_samples(addresses=["<a1>", "<a2>"], valueType="int32", byteOrder="big_endian")`. Cheat Engine 7.7
  also ships the custom types "2 Byte Big Endian", "4 Byte Big Endian" and "Float Big Endian" (off until ticked under
  Edit → Settings → Extra Custom Types; no 8-byte or double one). The scan tools cannot select a custom type and
  scan_next on main refuses one: the user runs those scans in Cheat Engine's window, then you read
  `scan_list_results(scannerName="main")`, whose values are shown in that type.
- Bytes you already hold: util_convert_value.byteOrder only changes how output bytes are shown; a numeric targetType is
  always decoded little-endian. Reverse the pairs first:
  `util_convert_value(value="00 00 03 E7", sourceType="bytes", targetType="bytes", byteOrder="big_endian")` returns
  `E7 03 00 00`, and `util_convert_value(value="E7 03 00 00", sourceType="bytes", targetType="int32")` gives 999.

## Guest and host addresses

| Emulator | Host address of a guest address |
| --- | --- |
| Dolphin | MEM1: emuBase + (guest - 80000000); Wii MEM2: its own view + (guest - 90000000) |
| PCSX2 | emuBase + (guest & 1FFFFFF) |
| PPSSPP | emuBase + guest (game RAM from 08800000) |
| RPCS3 | base + guest |
| Cemu | base + guest (game data from 10000000) |

`util_calculate(expression="0x80451230 - 0x80000000")` returns `451230` in util_calculate.hex, so the Dolphin guest
address 80451230 is `emuBase+451230`.

- Guest pointers hold guest addresses in guest byte order. Cheat Engine's chains (record_create.offsets,
  pointer_read_chain, pointer_find_paths) follow host pointers only, so follow a guest chain by hand: on Dolphin,
  `memory_read(address="emuBase+451230", valueType="bytes", size=4)` reading `80 45 67 00` points at guest 80456700,
  host `emuBase+456700`. Read as bytes, a big-endian guest pointer is its hex address in order.
- To find what points at a guest object, search its guest address in guest byte order; for the Dolphin object at
  emuBase+451230:
  `aob_find(patterns=["80 45 12 30"], startAddress="emuBase", endAddress="emuBase+17FFFFF", alignment=4, includeMapped=true)`.
- Records: `record_create(records=[{description="Coins", address="emuBase+3A1240", variableType=2}])`, then ask the
  user to set the record's type to "4 Byte Big Endian". The record tools cannot select a custom type, and until then
  Cheat Engine shows the byte-swapped number. Leave value out: a record writes it little-endian; write with
  memory_write and its byte order instead. A byte array record shows the bytes in guest order without any setting:
  `record_create(records=[{description="Coins bytes", address="emuBase+3A1240", variableType=8, length=4}])`
  ([cheat tables](cheat-tables.md)).

## Stability across restarts

- Static game data usually keeps its guest address on every boot of the same game version. Guest heap objects can
  move; reach them through a guest pointer in static data.
- The host base usually changes. Dolphin places it anew on each launch (since 5.0-3981). PCSX2 1.6 and earlier used
  the fixed host address 20000000; 1.7 moved it, and EEmem holds the current one. PPSSPP's base can change between
  games, while its pointer to the base stays valid. Cemu lets Windows pick its base. RPCS3 usually gets 300000000.
- Store records as `emuBase+offset`. After each emulator restart, find the base again. A registered symbol and a
  named scanner block attaching to the new process (busy): call `symbol_unregister(name="emuBase")` and
  `scan_delete(scannerName="emu")` before process_attach. symbol_register.doNotSave is true by default, so a reloaded
  table needs emuBase registered again. A pointer path from the emulator exe to the base ([pointers](pointers.md))
  holds only for that emulator build.

## Per-emulator notes

- **Dolphin** (GameCube, Wii; big-endian, mapped): MEM1 is 24 MiB at guest 80000000, in a mapped view of 0x2000000
  bytes (the next power of two; Dolphin's memory size override changes both). Wii MEM2 is 64 MiB at guest 90000000,
  a separate view. MEM1+0 holds the 6-character game ID, +18 the Wii magic `5D 1C 9E A3`, +1C the GameCube magic
  `C2 33 9F 3D`; a WAD title has neither. Other regions can share the size and fastmem maps the same RAM again
  elsewhere: keep a region that shows the magic, and use one base.
- **PCSX2** (PS2; little-endian, mapped): EE RAM is 32 MiB; IOP RAM (2 MiB) is separate. Builds since February 2022
  export EEmem (also IOPmem and VUmem) for debuggers, and since late 2022 EE RAM is a mapped view, so scans need
  includeMapped or the MEM_MAPPED setting. The exe name differs between builds: take it from module_list.
- **PPSSPP** (PSP; little-endian, mapped): RAM starts at PSP address 08000000 and game data at 08800000. From 1.15
  the user can copy the base with Debug → Copy PSP memory base address; a documented window message also returns it
  and a pointer to it. The RAM is mirrored at other guest addresses, so an unscoped scan can list a value more than
  once.
- **RPCS3** (PS3; big-endian, mapped): its source reserves guest memory at the first free 4 GiB boundary from host
  300000000, so the base is usually 300000000. An unprotected mirror of the same memory sits at base+100000000: scope
  scans to base up to base+100000000.
- **Cemu** 2.x (Wii U; big-endian, private): reserves 4 GiB wherever Windows allows and commits each area at base plus
  its guest address; game data (MEM2) is guest 10000000 to 4FFFFFFF. A private region of 0x40000000 bytes is a MEM2
  candidate, and its memory_list_regions.allocationBase is the base: confirm with a value you know.
- **Project64, mupen64plus** (N64): RDRAM is stored as host-order 32-bit words, so a 4-byte value reads and scans as a
  normal int32 with no byte order, a byte sits at its guest address XOR 3 and a 2-byte value at XOR 2.

## Dynarec and interpreter pitfalls

- "Find what writes" ([find_writer](../Workflows/find-writer.md)) on guest RAM lands in the interpreter, which stores
  every guest variable, or in JIT (dynarec) code that is recompiled and freed at any time. An injection there hits
  every variable or silently stops working: stay on the data side (records, freezes, writes).
- The same RAM can be reachable at several host addresses (Dolphin's JIT uses its fastmem views, RPCS3 keeps a
  mirror), so a write breakpoint on `emuBase+offset` can miss writes made through another view.
- For guest code, use the emulator's own debugger (Dolphin, PCSX2 and PPSSPP have one) and its cheat or patch format:
  Gecko and Action Replay (Dolphin), pnach (PCSX2), CWCheat (PPSSPP), RPCS3's patch manager. Each encodes guest
  addresses its own way (a code type in the top bits, or an offset from a RAM start): convert a found address with
  that format's documentation.
- One emulator process can run several games in turn: re-read the game ID before you save or reuse anything.

## Sources

- Cheat Engine source (https://github.com/cheat-engine/cheat-engine): `formsettingsunit.lfm` (MEM_MAPPED unticked),
  `memscan.pas` (region filter), `simpleaobscanner.pas` (AOBScan uses the same scanner), `CEFuncProc.pas` (global
  setting); local 7.7 `celua.txt` (`setSpecialScanOptionsOverride`) and `autorun/bigendian.lua`.
- Dolphin: https://github.com/aldelaro5/Dolphin-memory-engine/issues/68 and its `WindowsDolphinProcess.cpp`,
  `IDolphinProcess.h`; https://github.com/dolphin-emu/dolphin `Source/Core/Core/HW/Memmap.cpp`;
  https://tasvideos.org/Forum/Topics/17735 , https://wiibrew.org/wiki/Wii_disc
- PCSX2: https://github.com/PCSX2/pcsx2 `pcsx2/Memory.cpp` (EEmem export), issues 3638 and 7291, pull request 5531.
- PPSSPP: https://www.ppsspp.org/docs/reference/process-hacks/ ; https://github.com/hrydgard/ppsspp `Core/MemMap.cpp`,
  `Common/MemArenaWin32.cpp`
- RPCS3: https://github.com/RPCS3/rpcs3 `rpcs3/Emu/Memory/vm.cpp`;
  https://dennisstanistan.com/blog/82/how-to-use-cheat-engine-with-rpcs3/
- Cemu: https://github.com/cemu-project/Cemu `src/Cafe/HW/MMU/MMU.cpp`, `src/util/MemMapper/MemMapperWin.cpp`
- N64: https://github.com/project64/project64 `Source/Project64-core/N64System/Mips/MemoryVirtualMem.cpp`;
  https://github.com/mupen64plus/mupen64plus-core `src/osal/preproc.h`
