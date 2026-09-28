# Find values in an emulated console's memory

## Goal

Find the guest RAM of `{emulator}`, name its base, then find, read and (with consent) change values in the guest's
byte order; `{guestAddress}`, if given, comes from the user's notes. Offline play of owned games only.

## Steps

1. Stop if netplay or an online mode is on. `process_list(nameContains="{emulator}")`, then
   `process_attach(process="<exe>")`.
2. Find the base; page with memory_list_regions.nextOffset:
   - dolphin: `memory_list_regions(type="mapped", limit=2000)`: a region of 33554432 bytes (MEM1, guest 80000000)
     with the magic of step 3.
   - pcsx2: `module_list_exports(module="<exe>", nameContains="EEmem")`, then
     `memory_read(address="<export address>", valueType="pointer")`.
   - ppsspp: the user copies it (Debug, Copy PSP memory base address). rpcs3: usually 300000000.
   - cemu: `memory_list_regions(type="private", format="detailed", limit=2000)`: the allocationBase of a region of
     1073741824 bytes. other: a region sized like the console's RAM.
3. Check it: `memory_read(address="<base>+1C", valueType="bytes", size=4)` reads `C2 33 9F 3D` on a GameCube disc
   (Wii: `5D 1C 9E A3` at +18; game ID at +0, re-read after a game change). Elsewhere read nonzero bytes in
   step 7's range; a value found there confirms it.
4. Tell the user, then `symbol_register(name="emuBase", address="<base>")`.
5. `{guestAddress}` is at emuBase + guest, minus 80000000 on Dolphin (the `hex` of
   `util_calculate(expression="0x<guest> - 0x80000000")`) and & 1FFFFFF on PCSX2. Read it:
   `memory_read(address="emuBase+<offset>", valueType="int32", byteOrder="big_endian")` (little_endian: pcsx2, ppsspp).
6. Scans skip mapped memory (all guest RAM here but Cemu's). Test includeMapped on step 3's bytes:
   `aob_find(patterns=["<those bytes>"], startAddress="<their address>", endAddress="<their address>+FF", limit=2, includeMapped=true)`
   must match; if not, see Decisions.
7. Search guest RAM only (end exclusive): dolphin to emuBase+1800000, pcsx2 +2000000, ppsspp +8800000 to +A000000,
   cemu +10000000 to +50000000, rpcs3 +100000000. Little-endian:
   `scan_first(scannerName="emu", valueType="int32", value="100", startAddress="emuBase", endAddress="emuBase+2000000", includeMapped=true)`.
   Big-endian: `util_convert_value(value="100", sourceType="int32", targetType="bytes", byteOrder="big_endian")`
   gives `00 00 00 64`;
   `scan_first(scannerName="emu", valueType="bytes", value="00 00 00 64", startAddress="emuBase", endAddress="emuBase+1800000", alignment=4, includeMapped=true)`,
   then `scan_next(scannerName="emu", comparison="exact", value="00 00 00 5F")` when it is 95; list with
   scan_list_results.
8. With consent:
   `memory_write(address="emuBase+<offset>", valueType="int32", value="999", byteOrder="big_endian", verify=true)`;
   undo by writing memory_write.previous back as valueType bytes.
9. Keep: `record_create(records=[{description="Coins", address="emuBase+<offset>", variableType=2}])`, without value.
   Big-endian: only the user can set its type to "4 Byte Big Endian" (Edit, Settings, Extra Custom Types).

## Decisions

- No match in step 6, or includeMapped unsupported:
  `memory_create_snapshot(name="ram-a", address="emuBase", size=16777216)`, then
  `memory_compare_snapshot(name="ram-a", valueType="int32", change="changed")`. For main, the user ticks MEM_MAPPED
  (Edit, Settings, Scan Settings).
- Unknown values:
  `scan_first(scannerName="emu", valueType="int32", comparison="unknown", startAddress=..., endAddress=..., alignment=4, includeMapped=true)`,
  then `changed` or `unchanged`; `increased` and `decreased` compare little-endian numbers, so on a big-endian
  guest watch candidates: `memory_read_samples(addresses=["<a1>"], valueType="int32", byteOrder="big_endian")`.
- Guest pointers are guest addresses in guest byte order: read them as bytes, follow them by hand.
- Stay on the data side: find what writes lands in emulator code.

## Pitfalls

- byteOrder: memory reads and writes only, int16 to double. Scans, aob_find_value, snapshots and pointer chains stay
  little-endian, and util_convert_value.byteOrder only orders the bytes it prints.
- includeMapped ends every CE scan-region override, a table's or Lua's too; parallel first scans must agree on it
  (busy). After `cleanup_unconfirmed`, the next includeMapped scan ends the override.
- The host base usually moves on each launch, and records on `emuBase` resolve only while the symbol exists (a
  saved table omits it): find the base again after `symbol_unregister(name="emuBase")` and
  `scan_delete(scannerName="emu")`, which block attaching.

## Report

A table of guest address, `emuBase+offset`, byte order, value and record id. What remains: `emuBase` and the scanner
(undo in Pitfalls), the snapshot until `memory_delete_snapshot(name="ram-a")`, the records, and MEM_MAPPED if the
user ticked it. Background: [emulators](../Documents/emulators.md) and [value types](../Documents/value-types.md).
