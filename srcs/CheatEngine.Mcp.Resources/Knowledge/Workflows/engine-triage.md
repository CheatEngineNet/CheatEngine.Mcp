# Identify the game engine and how it stores values

## Goal

Identify the engine or runtime of the attached game from its modules, exports and version text, predict how it stores
numbers, and choose the anchor and the next workflow for `{goal}`. Every step only reads.

## Steps

1. If not asked yet, ask whether the game is offline and free of anti-cheat; stop otherwise
   ([safety](../Documents/safety.md)).
2. `process_get_current()`: `processName` and `pointerSize` (4: a 32-bit target, so 64-bit layouts and offsets do not
   apply). No target: run [attach and orient](attach-and-orient.md) first.
3. `module_list(format="detailed", limit=200)`: the runtime DLLs and the game folder; page with `offset` while
   `module_list.nextOffset` is present.
4. `module_get(module="<processName>")`: `pe.managed` true means a .NET Framework executable (.NET Core and 5+ start
   from a native host, so look for `coreclr.dll`); keep `pe.timeDateStamp`.
5. Exports: `module_list_exports(module="GameAssembly.dll", nameContains="il2cpp_")` confirms IL2CPP,
   `module_list_exports(module="<the mono DLL>", nameContains="thread_attach")` Mono.
6. Version text: `aob_find_value(valueType="wstring", value="++UE", module="<processName>", limit=5)`, then
   `memory_read(address="<match>", valueType="wstring", length=64)` reads `++UE4+Release-4.27` or
   `++UE5+Release-5.3` (it may be ANSI: search as `string` too). Godot:
   `aob_find_value(valueType="string", value="Godot Engine", module="<processName>", limit=2)`.
7. Emulators: `memory_list_regions(type="mapped", format="detailed", limit=100)` may show a mapped view sized like
   the console's RAM (Cemu keeps it in `private` memory).
8. `process_list(nameContains="<name>")`: several processes mean a launcher beside the game or Chromium renderers;
   ask which one runs the game. Ask too about files the tools cannot list: `data.win`, `*.pck`, `www/js`,
   `resources/app.asar`, `<Game>_Data/il2cpp_data`.

## Decisions

| Evidence | Engine | Numbers | Next |
|---|---|---|---|
| `mono-2.0-bdwgc.dll`, `mono.dll` | Unity, Mono | C# types | [Unity Mono](unity-mono-recon.md) |
| `GameAssembly.dll`, `il2cpp_` exports | Unity, IL2CPP | C# types | [Unity IL2CPP](unity-il2cpp-recon.md) |
| `coreclr.dll`, `clr.dll`, `pe.managed` | .NET (XNA, FNA, MonoGame) | C# types, moved by GC | [.NET](dotnet-recon.md) |
| `*-Win64-Shipping.exe`, `++UE4+`, `++UE5+` | Unreal | float; UE5 positions double | [Unreal](unreal-recon.md) |
| `Godot Engine`, a `.pck` | Godot (C#: Mono SGen, .NET) | script int64, double; positions float | scan |
| `lua51.dll`, `lua53.dll`, `lua54.dll` | Lua, LÖVE | 5.1: double; 5.3+: int64 or double | scan |
| `data.win` | GameMaker | double | scan |
| `RGSS*.dll` | RPG Maker XP, VX, VX Ace | int32 2n+1 | scan |
| `nw.dll`, `libcef.dll` | Chromium, NW.js, RPG Maker MV, MZ | double, int32 2n (older: n) | scan |
| `engine.dll`, `server.dll` | Source | int32, float | scan |
| `jvm.dll` | Java | objects move | CE's Java menu |
| emulator, large mapped view | emulator | guest RAM, maybe big-endian | [emulator](emulator-memory.md) |
| none of these | native C++ | declared C++ types | scan |

- scan: [find a known value](find-known-value.md) with the predicted type.
- `UnityPlayer.dll` alone does not tell Mono from IL2CPP: look again once the game has loaded.
- The Mono and IL2CPP recon needs TargetCodeExecution (`runtime_get_info.gates`) to attach the collector, unless
  `mono_get_status.attached` is true; otherwise scan.
- Anchor on what the engine provides: a Mono static field, `GameAssembly.dll`+RVA, Unreal's `GWorld`, an emulator's
  RAM base registered as a symbol. Prefer these to blind pointer scans.
- Interpreters, JITs and emulators (GameMaker VM, GDScript, Ruby, JavaScript, Java, Flash): a writer lands in shared
  interpreter code, so stay on the data side.
- Source: health has a `server.dll` copy and a `client.dll` copy; keep the one `server.dll` writes.

## Pitfalls

- The wrong type finds nothing: a 4-byte exact scan misses doubles and tagged integers (500 gold in RPG Maker XP is
  1001).
- "Obscured" or encrypted values, DRM or licence checks, or a module or process named after an anti-cheat: tell the
  user and stop.
- Offsets from another game or build do not carry over.
- Dumpers and Java agents inject into the game: the user's decision; these tools never run them.

## Report

The engine and version with its evidence, `pointerSize`, the predicted value representation, the anchor, the next
workflow for `{goal}`, and the user's answer on online play and anti-cheat. Nothing to undo. Background:
[game engines](../Documents/game-engines.md), [Mono and .NET](../Documents/mono-and-dotnet.md).
