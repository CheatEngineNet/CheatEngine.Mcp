# Game engines and runtimes

The engine or runtime a game uses decides how it stores numbers, which anchor survives a restart, and which workflow to
run next. Read this page right after attaching and before the first scan, because a scan with the wrong type finds
nothing. The guided version is [engine_triage](../Workflows/engine-triage.md).

## Scope first

- Work only on single-player or offline games the user owns or may modify ([safety](safety.md)). Unreal and Unity
  games with online or co-op modes often ship anti-cheat (Easy Anti-Cheat, BattlEye, kernel drivers), even for offline
  play. Ask before attaching (if already attached, before anything else), and stop when the game is online or
  protected.
- "Obscured" or encrypted value types, DRM and licence checks mean the developer opposes modification. Tell the user
  and stop; do not decode or bypass them.
- A save editor, the game's own console or its mod support is often simpler than memory editing. Offer it.

## Collect the evidence

Every step below only reads.

1. `process_get_current()` gives `processName` and `pointerSize`. A value of 4 means a 32-bit target: the x64 layouts
   below (Unity, .NET, Unreal, Java) do not apply.
2. `module_list(format="detailed", limit=1000)` gives the runtime DLLs and their paths, which show the game's folder
   layout. Filter with a call such as `module_list(nameContains="mono")`.
3. `module_get(module="<game>.exe")`: `pe.managed` is true for a .NET Framework executable (XNA, older FNA and
   MonoGame builds). .NET Core and .NET 5+ games start from a native host exe, so look for `coreclr.dll` instead. Keep
   `pe.timeDateStamp` to tell later whether the game was updated.
4. Runtime exports: `module_list_exports(module="GameAssembly.dll", nameContains="il2cpp_")` for IL2CPP, or
   `module_list_exports(module="mono-2.0-bdwgc.dll", nameContains="thread_attach")` for Mono. CE's Mono script finds
   the runtime the same way, by its `mono_thread_attach` or `il2cpp_thread_attach` export.
5. Version text in the main image:
   `aob_find_value(valueType="wstring", value="++UE", module="<Project>-Win64-Shipping.exe", limit=5)`, then
   `memory_read(address="<match>", valueType="wstring", length=64)`, which reads like `++UE5+Release-5.3`. The text
   may be ANSI instead: repeat both calls with `string`. Godot:
   `aob_find_value(valueType="string", value="Godot Engine", module="<game>.exe", limit=5)`, then read each match as
   `string`; one may continue with the version, such as `Godot Engine v4.2.1.stable.official`.
6. `process_list(nameContains="<name>")` reveals several processes: Unreal's small `<Project>.exe` launcher beside the
   real `<Project>-Win64-Shipping.exe`, or the browser, GPU and renderer processes of a Chromium-based game. It reports
   only ids and names, so ask the user which process is the game (Task Manager shows memory use and, as an optional
   column, the command line).
7. Emulators: `memory_list_regions(type="mapped", format="detailed")` shows a mapped view sized like the console's RAM.
   Cemu keeps guest RAM in private memory instead: `memory_list_regions(type="private", format="detailed")`.
8. No tool lists the game folder. Ask the user about files such as `data.win`, `*.pck`, `Content/Paks`, `www/js`,
   `resources/app.asar` or `<Game>_Data/il2cpp_data`.

## Identification table

| Evidence | Engine or runtime | Numbers | Go to |
|---|---|---|---|
| `mono-2.0-bdwgc.dll` or `mono.dll`, often with `UnityPlayer.dll` | Unity, Mono | C# types | [unity_mono_recon](../Workflows/unity-mono-recon.md) |
| `GameAssembly.dll`, `il2cpp_` exports | Unity, IL2CPP | C# types | [unity_il2cpp_recon](../Workflows/unity-il2cpp-recon.md) |
| `coreclr.dll` or `clr.dll` | .NET (XNA, FNA, MonoGame) | C# types | [.NET recon](../Workflows/dotnet-recon.md) |
| `*-Win64-Shipping.exe`, `++UE4+` or `++UE5+` text | Unreal Engine | float; UE5 double vectors | [unreal_recon](../Workflows/unreal-recon.md) |
| one exe, `Godot Engine` text, a `.pck` | Godot | script int64 and double | Godot, below |
| `data.win` beside the exe | GameMaker | double | GameMaker, below |
| `RGSS*.dll` | RPG Maker XP, VX, VX Ace | int32 2n+1 | RPG Maker, below |
| `nw.dll`, `www/js/rpg_*.js` or `js/rmmz_*.js` | RPG Maker MV, MZ | double, V8 small integers | RPG Maker, below |
| `engine.dll`, `client.dll`, `server.dll`, `tier0.dll` | Source | int32, float | Source, below |
| `jvm.dll` | Java (HotSpot) | objects move | Java, below |
| Flash projector exe, `Adobe AIR.dll` | Flash, AIR | int32 8n+6, double | Flash, below |
| same-name processes, `libcef.dll`, `nw.dll`, `resources/app.asar` | Chromium, V8 | double, V8 small integers | Chromium, below |
| `lua51.dll` (Lua 5.1 or LuaJIT, as in LÖVE), `lua53.dll`, `lua54.dll` | Lua | 5.1: double; 5.3+: int64 or double | [find_known_value](../Workflows/find-known-value.md) |
| Dolphin, PCSX2, PPSSPP, RPCS3, Cemu | emulator | guest RAM, byte order | [emulator_memory](../Workflows/emulator-memory.md) |
| none of these: a C++ exe and its DLLs | native engine | C++ types | [find_known_value](../Workflows/find-known-value.md) |

- `UnityPlayer.dll` alone does not yet tell you the backend. Look again once the game has loaded.
- CE's own detectors agree. `monoscript.lua` shows CE's Mono menu when a module is named `mono.dll`, `mono-*`,
  `libmono*`, `libil2cpp*`, `GameAssembly.dll` or `UnityPlayer.dll`, or, for .NET, `clr.dll`, `coreclr.dll` or
  `clrjit.dll`. `java.lua` shows its Java menu when `jvm.dll` is loaded.

## The general procedure

1. **Identify** the engine from the evidence above.
2. **Predict the representation** before scanning: int32, int64, float, double, a tagged integer (2n, 2n+1, 8n+6), or
   big-endian. A 4-byte exact scan on a double or tagged engine finds nothing; this is the most common failure
   ([value-types](value-types.md#tagged-and-engine-specific-numbers)).
3. **Scan, then verify** with the engine's own metadata: a Godot hit has type 2 or 3 at hit-8, a GameMaker hit has
   kind 0 at hit+0xC, and `mono_get_object(address="<hit>")` names the Unity object that holds a hit and the field at
   its `offsetInObject`.
4. **Anchor** on what the engine provides: a Mono static field, `GameAssembly.dll`+RVA in IL2CPP, Unreal's `GWorld`,
   or an emulator's RAM base plus an offset. Prefer these to blind multi-level pointer scans ([pointers](pointers.md)).
5. **Choose data over code in interpreters.** In GameMaker's VM, GDScript, Ruby, Flash, JavaScript, Java and
   emulators, [find_writer](../Workflows/find-writer.md) lands in interpreter, JIT or dynarec code shared by many
   variables. A patch there changes all of them, or nothing once the code is recompiled: edit the data instead, or
   filter hard by object ([shared_code_filter](../Workflows/shared-code-filter.md)).

## Positions and axes

[find_position](../Workflows/find-position.md) needs the type and the up axis:

| Engine | Position type | Axes |
|---|---|---|
| Unity | float, in native Transform memory | Y up; one unit is a metre by convention |
| Unreal | UE4 float, UE5 double | Z up; one unit is a centimetre |
| Godot | float (double in double-precision builds) | 3D: Y up; 2D: Y grows downward |
| Source | float | Z up |
| GameMaker | float built-in `x` and `y` | 2D pixels; y grows downward |

## Unity: Mono and IL2CPP

- Mono uses `mono-2.0-bdwgc.dll` (the .NET 4.x runtime, under `MonoBleedingEdge/EmbedRuntime/`) or the legacy
  `mono.dll` (under `<Game>_Data/Mono/`). IL2CPP puts `GameAssembly.dll` at the top level and ships
  `<Game>_Data/il2cpp_data/Metadata/global-metadata.dat`.
- Both use the same x64 object layout: a 0x10-byte header (vtable or class pointer, then monitor), so the first
  instance field is usually at +0x10. `System.String` has its int32 length at +0x10 and UTF-16 text at +0x14; an array
  has its length at +0x18 and elements from +0x20; structs are inline, without a header.
- `UnityEngine.Object` wrappers point at native C++ objects (`m_CachedPtr`). Transforms and positions live in native
  memory, not in C# fields. Unity's garbage collector does not move objects, but scene changes destroy and recreate
  them.
- **Mono route.** With consent, `mono_attach()` injects CE's data collector (targetCodeExecution gate) and changes CE
  state: it can set the table's Uses Mono option, after which CE injects the collector again on each process open
  (`process_attach.monoAutoAttach` reports that case; [mono-and-dotnet](mono-and-dotnet.md)). Then
  `mono_find_class(namespace="", className="Player")` and
  `mono_list_fields(classHandle="<handle>", includeParents=true)` give offsets and `staticAddress`;
  `mono_list_fields(classHandle="<handle>", nameContains="health")` also matches `<Health>k__BackingField`. Static
  storage lives outside any module, so re-resolve it through Mono each session instead of pointer-scanning it.
  `mono_detach()` closes only the attachment MCP made; the collector DLL and the Uses Mono option remain.
- **From a scan hit.** With the collector attached, `mono_get_object(address="<hit>")` walks down from the hit, at
  most `mono_get_object.maxBacktrack` bytes (default 4096), by reading memory only; the collector is never asked
  about the address. It returns the object start `address`, `className`, `namespace`, `classHandle`,
  `offsetInObject` and the fields with their values: the field whose offset equals `offsetInObject` holds the hit.
  Generic-instance and array objects are refused as `unsupported`, and so is an IL2CPP image whose classes the
  collector cannot list. On a large assembly it can block CE for seconds.
- **IL2CPP route.** The code is native at fixed RVAs within one build, so `GameAssembly.dll`+RVA signatures and
  injections hold until the game updates. `aob_generate_signature(address="<instruction>")` also works when
  `GameAssembly.dll` exceeds 64 MiB: a managed generator then builds the pattern
  ([aob-signatures](aob-signatures.md)). `mono_attach()` on an IL2CPP game gives CE's partial IL2CPP mode
  (`mono_get_status.il2Cpp` is true): classes, fields and methods, but no IL or JIT. External dumps are the user's
  choice ([unity-il2cpp](unity-il2cpp.md)).

## Other .NET games

XNA, FNA and MonoGame games run on .NET (`clr.dll` for .NET Framework, `coreclr.dll` for .NET Core and .NET 5+). CE
reads them through a separate helper process, so nothing is injected: `dotnet_get_status()`, then
[.NET recon](../Workflows/dotnet-recon.md). A .NET object starts with its MethodTable pointer, so fields begin at +0x8
(`System.String`: length at +0x8, UTF-16 text at +0xC). The garbage collector compacts the heap and objects move, so
anchor on static fields (`dotnet_get_type` reports their `staticAddress`), not on heap addresses.
`dotnet_list_types(moduleHandle="<moduleHandle>", nameContains="Player")` finds a type by part of its full name, and
`dotnet_get_object(address="<object start>")` reads an object's type and fields; it serves .NET only, so use
`mono_get_object` on Unity. A NativeAOT build loads no CLR module: treat it as native code.

## Unreal Engine 4 and 5

- Attach to `<Project>-Win64-Shipping.exe` (under `<Project>/Binaries/Win64/`), not to the launcher beside it:
  `process_list(nameContains="Shipping")`.
- Version: the `++UE4+Release-4.27` or `++UE5+Release-5.3` text (step 5 above).
- UE4 vectors and rotators are float. UE5 Large World Coordinates make FVector, FVector2D, FRotator, FQuat,
  FTransform, FMatrix, FPlane and FBox doubles: scan UE5 positions as `double`. Health and stats are usually float.
- A shipping build's main module holds the globals `GUObjectArray` (every UObject), `FNamePool` (names, UE 4.23+) and
  `GWorld`. The usual chain is `GWorld` → game instance → local player → player controller → pawn, but every offset
  belongs to one build: take it from reflection or the user's SDK dump, never from another game
  ([unreal-engine](unreal-engine.md)).
- Shipping executables often exceed 64 MiB. There `aob_generate_signature(address="<instruction>")` uses its managed
  generator (`aob_generate_signature.generator` is `managed`); review the pattern before you rely on it.
- External dumpers (Dumper-7, UE4SS) inject into the game. Running them is the user's decision; these tools never do.

## Godot

- The engine is linked into the exe; the game data is a `.pck` beside the exe or appended to it.
- GDScript values are `Variant`s: an int32 type tag at +0 and the payload at +8. INT is **int64** and FLOAT is
  **double** in Godot 3 and 4; the type ids are NIL 0, BOOL 1, INT 2, FLOAT 3 (REAL in Godot 3), STRING 4.
- Scan with `scan_first(valueType="int64", value="100")` or as `double`, then verify the hit:
  `memory_read(address="<hit>-8", valueType="int32")` returns 2 for an int, 3 for a float.
- Built-in node properties such as position are float32 fields of native objects (double only in double-precision
  builds).
- C# projects also load .NET (`coreclr.dll`, Godot 4) or Mono (`mono-2.0-sgen.dll` in Godot 3, unless linked into
  the exe): check `module_list`. Mono's SGen collector, unlike Unity's, moves young objects, so anchor on statics
  there too. Whether CE's collectors work there is UNVERIFIED.

## GameMaker

- Evidence: `data.win` beside the runner exe. YYC builds compile the game to native code; VM builds interpret
  bytecode.
- Nearly every GML number is a **double** in a 16-byte RValue: payload at +0, flags at +8, kind at +0xC (0 real,
  1 string, 2 array, 6 object, 7 int32, 10 int64, 13 bool).
- Scan with `scan_first(valueType="double", value="100")`, then check the kind:
  `memory_read(address="<hit>+C", valueType="int32")` returns 0.
- Built-in instance variables such as `x`, `y` and `direction` are float fields of the runner's instance object, not
  RValues: scan them as `float`.
- User variables sit in version-dependent hash maps whose addresses change when a map grows. Anchor at a static in the
  runner exe and check the path again after each game update.

## RPG Maker

- XP, VX and VX Ace (`RGSS*.dll`, 32-bit Ruby) store an integer n as **2n+1**; false is 0, true 2, nil 4. For 500
  gold, `scan_first(valueType="int32", value="1001")`, and write 2m+1. Pointer paths usually root in a static of
  `RGSS*.dll`.
- MV and MZ run on NW.js (`nw.dll`). Attach to the renderer process, scan `double` first, then the V8 small-integer
  forms (Chromium, below). Values move when the garbage collector runs.
- Editing the save is often simpler: MV `.rpgsave` and MZ `.rmmzsave` files hold compressed JSON.

## Source engine

- Modules: `engine.dll`, `client.dll`, `server.dll`, `tier0.dll`, `vstdlib.dll`; the DLLs export `CreateInterface`.
- Single-player runs a listen server in the same process: `server.dll` owns the real entity and `client.dll` a
  networked copy. A health scan returns at least two hits, and a write to the client copy is overwritten.
- Keep the hit whose writer is in `server.dll`: [find_writer](../Workflows/find-writer.md), then
  `memory_get_address_info(addresses=["<instruction address>"])` names the module. Root pointer paths in `server.dll`.
- Multiplayer Source games use VAC: out of scope.

## Java

- Evidence: `jvm.dll` (java.exe, javaw.exe or a bundled runtime).
- The garbage collector moves objects, so raw addresses and pointer scans do not survive. Below a 32 GB heap,
  references are 4 bytes (address = heap base + reference × 8); the 12-byte header puts the first field near +0x0C;
  the JVM reorders fields by size. JIT code moves, so avoid code injection.
- CE's **Java** menu offers **Java Info** (a class, field and method browser) and **Java variable scan**, which scans
  field values through the JVM and returns object references, so its results survive GC moves. Both first inject CE's
  JVMTI agent (`CEJVMTI.dll`) into the game. No tool here drives that menu: the user does, after deciding to inject.

## Flash and AIR

- AVM2 tags values in their low 3 bits: an int atom is **8n+6**, false is 5, true is 13, and a Number atom points to
  a boxed double. For 100, `scan_first(valueType="int32", comparison="between", value="800", upperValue="807")`, and
  write 8m+6. Typed slots may hold a plain int32 or double (UNVERIFIED): try those too.
- On 32-bit players an int atom holds 29 bits; larger values become doubles. Ruffle, a Flash emulator, stores values
  its own way (UNVERIFIED): try plain int32 and double.

## Chromium, Electron, NW.js and HTML5

- The game runs in a **renderer** process (command line `--type=renderer`), usually the one using the most memory.
  Ask the user to confirm it in Task Manager.
- With V8 pointer compression (Chrome 80+; Electron 14+ per its blog) and in 32-bit processes, small integers are
  int32 **2n** (Smi) and heap pointers have the low bit set. In a 64-bit build without compression, n sits in the
  upper half of an 8-byte slot, so an int32 scan for n finds it. Fractions and large numbers are boxed doubles. Scan
  `double` first, then int32 2n or n, and find values again each session: objects move.
- Better routes the user controls: save files, localStorage, developer tools. WebAssembly keeps state in one
  linear-memory buffer of little-endian values: use its base plus offsets, as for an emulator (base stability
  UNVERIFIED).

## Emulators

- Guest RAM usually lives in a mapped view (MEM_MAPPED); Cemu uses private memory. Reads and writes reach either. Find
  the base ([emulators](emulators.md)), register it, `symbol_register(name="emuBase", address="<RAM base>")`, and
  address values as `emuBase+offset`. After an emulator restart, `symbol_unregister(name="emuBase")` and register the
  new base. Guest pointers (`80xxxxxx` on GameCube) are not host addresses.
- The main scanner skips mapped memory until the user ticks MEM_MAPPED in CE's Scan Settings. A named scan includes it
  for one call, and `aob_find.includeMapped` and `aob_find_value.includeMapped` do the same without a module:
  `scan_first(scannerName="emu", valueType="int32", value="100", startAddress="emuBase", endAddress="emuBase+<RAM size>", includeMapped=true)`.
  Such a call also ends a scan-region override that a table or `lua_execute` set, and the named scanner blocks a
  target switch until `scan_delete(scannerName="emu")`
  ([troubleshooting-scans](troubleshooting-scans.md#emulators-and-mapped-memory)).
- Big-endian: GameCube and Wii (Dolphin), Wii U (Cemu), PS3 (RPCS3), Xbox 360. Little-endian: PS1, PS2 (PCSX2), PSP
  (PPSSPP), GBA, DS, Switch. Memory tools read and write 2-, 4- and 8-byte numbers in guest order:
  `memory_read(address="emuBase+3A1240", valueType="int32", byteOrder="big_endian")`, and after consent
  `memory_write(address="emuBase+3A1240", valueType="int32", value="999", byteOrder="big_endian", verify=true)`. The
  batch items and `memory_read_samples` take the same option.
- Scans have no byte order. Convert an exact value:
  `util_convert_value(value="100", sourceType="int32", targetType="bytes", byteOrder="big_endian")` returns
  `util_convert_value.bytes` `00 00 00 64`, then
  `scan_first(scannerName="emu", valueType="bytes", value="00 00 00 64", startAddress="emuBase", endAddress="emuBase+<RAM size>", includeMapped=true)`.
  Increased and decreased scans and snapshot comparisons compare little-endian numbers, and the scan tools cannot
  select CE's big-endian custom types (`bigendian.lua`) ([value-types](value-types.md#big-endian-data-in-emulators)).
- [find_writer](../Workflows/find-writer.md) lands in the emulator's interpreter or dynarec: stay on the data side, or
  use the emulator's own cheat format. Bases per emulator: [emulators](emulators.md),
  [emulator_memory](../Workflows/emulator-memory.md).

## When nothing matches

- Native C++ engines store declared C++ types: start with [find_known_value](../Workflows/find-known-value.md) or
  [find_unknown_value](../Workflows/find-unknown-value.md).
  `memory_get_address_info(addresses=["<object>"], includeRtti=true)` often names MSVC classes, and
  [structures](structures.md) lays out the object.
- A scan that still finds nothing: [troubleshooting-scans](troubleshooting-scans.md).

## Sources

- Cheat Engine 7.7, local: `autorun/monoscript.lua` (runtime detection), `autorun/java.lua` (Java menu, JVMTI agent),
  `autorun/bigendian.lua`, `celua.txt`.
- Unreal: https://github.com/cheat-engine/UnrealEngineTools , https://github.com/AniLeo/ue-version (build stamp),
  https://dev.epicgames.com/documentation/unreal-engine/large-world-coordinates-in-unreal-engine-5
- Unity: https://github.com/dreamanlan/il2cpp_ref/blob/master/libil2cpp/il2cpp-object-internals.h ,
  https://docs.unity3d.com/Manual/scripting-backends.html
- Mono SGen: https://www.mono-project.com/docs/advanced/garbage-collector/sgen/
- Godot: https://docs.godotengine.org/en/stable/engine_details/architecture/variant_class.html ,
  https://github.com/godotengine/godot (`core/variant/variant.h`, `core/version.h`, `modules/mono`)
- GameMaker: https://github.com/AurieFramework/YYToolkit (RValue, CInstance)
- RPG Maker: https://fearlessrevolution.com/viewtopic.php?t=5736
- V8 and Electron: https://v8.dev/blog/pointer-compression , https://www.electronjs.org/blog/v8-memory-cage
- Flash: https://github.com/adobe/avmplus (core/atom.h)
- Java: https://wiki.openjdk.org/spaces/HotSpot/pages/11829259/CompressedOops
- Emulators: https://github.com/aldelaro5/Dolphin-memory-engine/issues/68 , https://github.com/cemu-project/Cemu
  (MMU.cpp: guest memory is private)
