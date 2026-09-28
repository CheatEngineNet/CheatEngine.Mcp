# Address list records and cheat tables

Cheat Engine's address list holds records: values to watch or freeze, pointer chains, groups and Auto Assembler
scripts. A cheat table (`.CT`, XML) saves them with scripts, structures and an optional Lua script. Read this page
before you create, change, activate, delete, load or save records. The address list belongs to Cheat Engine: records
and freezes survive a plugin disable, and `runtime_release_resources` does not touch them.

## Responsible use

- Work only on single-player or offline software the user owns or may modify; never on online, competitive or
  anti-cheat-protected games. See [safety](safety.md).
- Explain and ask before each change: a create or update value writes target memory, activation freezes a value or
  runs a script, delete and clear cannot be undone, a table load can run Lua and inject code, and an overwrite
  replaces a file.

## Record fields and types

Record reads and record changes return the same record object: `id`, `index` (position in the whole list),
`description`, `address` (as typed), `value` (the text Cheat Engine shows), `variableType`, `script`, `offsetCount`,
`offsets`, `currentAddress` (when it resolves), `active`, `childCount`, `asyncProcessing`, and `dropdown` on request.
`record_set_active`, `record_delete` and `record_clear` return their own results. `variableType` is Cheat Engine's
`vt` code:

| Code | Cheat Engine type | Memory `valueType` | Notes |
|---|---|---|---|
| 0 | Byte | `int8`, `uint8` | |
| 1 | 2 Bytes | `int16`, `uint16` | |
| 2 | 4 Bytes | `int32`, `uint32`, `pointer` on 32-bit | the default |
| 3 | 8 Bytes | `int64`, `uint64`, `pointer` on x64 | |
| 4 | Float | `float` | |
| 5 | Double | `double` | |
| 6 | String | `string`, `wstring` | length in characters; UTF-16 (`wstring`) with the unicode option |
| 7 | Unicode string (autoguess only) | none | refused: use 6 with unicode |
| 8 | Array of byte | `bytes` | hex byte pairs (the tools turn hex display on), length in bytes |
| 9 | Binary | none | bit fields; the tools set no bit range |
| 10 | All (scan only) | none | refused: use the value's own type |
| 11 | Auto Assembler script | none | address `"0"`, no value, `script`; needs Mcp:EnableAutoAssembler |
| 12 | Pointer (autoguess and structures only) | none | refused: 3 on x64, 2 on 32-bit, `offsets` for a chain |
| 13 | Custom | none | the tools cannot choose the custom type |
| 14 | Grouped (a scan type) | none | the tools' group record: address `"0"`, no value (below) |

- `record_create` and `record_update` refuse 7, 10 and 12 (`invalid_argument` with a hint). `record_find` still
  accepts them, since a loaded table or Lua can make such records; their value reads as empty text (Cheat Engine's
  source, unverified live on 7.7). A pointer chain is a value record with `offsets`.
- 14 is not Cheat Engine's own group header, a flag the tools cannot set (the user makes one with Add group). It works
  as a parent, but Cheat Engine's source saves its type as `Error`, so after a table reload it is a Byte record at
  address `0` that shows `??`, with its children intact.
- No tool sets hex or signed display of a number, a bit range, a custom type, colour, hotkeys, a freeze direction,
  the group-header flag or the child options. The user sets them in Cheat Engine, or, with consent, `lua_execute`
  through the `MemoryRecord` Lua class (`ShowAsHex`, `ShowAsSigned`, `Binary.Startbit`, `AllowIncrease`,
  `IsGroupHeader`, `Options`, `createHotkey`). Byte layouts: [value types](value-types.md).

## Read the address list

- `record_list(offset=0, limit=100)` pages the whole list in pre-order: each record is followed by its nested records,
  collapsed groups included, and `total` counts every record (limit up to 1000; no `nextOffset` on the last page).
- `record_list(parentId=<id>)` lists only a record's immediate children. Only when a table load reused a listed
  child's id does it copy the whole subtree to renew the ids (at most 4096 records and 64 levels, else refused).
- `record_get(ids=[<id>, <id>])` copies 1 to 256 records by id, nested or not; one unknown id fails the whole read.
- `record_find(descriptionContains="health")` or `record_find(variableType=11, active=true)` searches every record,
  nested ones included, with at least one predicate; an address predicate matches the expression text, not the
  resolved address. Above 4096 records, nested ones counted, it refuses. Narrow the predicates when `truncated` is true.
- `record_get_selected()` returns the user's selection (`selected`, `record`): start there when the user says "this
  one". `record_select(id=<id>)` changes that visible selection; use it only to show the user a record.
- `cheatengine://instance/records` serves `record_list` pages, and `cheatengine://instance/records/{recordId}` one
  record as `record_get` returns it.
- Ids belong to one Cheat Engine instance; never reuse one from memory after a gap. A `table_load` that reaches Cheat
  Engine (merge or replace, even a failed one) retires every earlier id: it fails with `invalid_state` until a new
  listing returns it. Loads and edits made in Cheat Engine are not detected; a vanished record is `not_found`.

## Create records

`record_create(records=[...])` creates 1 to 256 records in order and returns them. Each item needs `description` (may
be empty) and `address`; `value` is optional. The other keys are `variableType` (default 2), `parentId`, `script`
(type 11 only), `offsets`, `length` and `unicode`.

- Each record is built without touching memory (description, address and type, then parent, string or byte array
  layout, script and offsets); only then is a non-empty `value` written, at the record's final address. An empty or
  omitted value writes nothing for any type. A value writes target memory: ask first.
- Types 0 to 5 and 13 take only a plain number: decimal (`100`, `-5`, `1.5`, `1.5e3`) or hex with `0x` or `$`.
  Anything Cheat Engine would evaluate as Lua is refused with `invalid_argument` before any change: brackets, a
  `+ - * /` after the first character (`1+2`, `1e-5`) or `(description)`. A record a table set to hex display reads
  plain digits as hex.
- A string (6) takes its text. A byte array (8) takes hex pairs such as `48 8B 05` (`??` keeps a byte) and is sized
  to the larger of its length and the bytes in the value.
- `length` (1 to 4096) counts characters for 6 and bytes for 8; `unicode` applies only to 6. A string or byte array
  created without a value needs `length`, or it fails with `invalid_argument`.

```text
record_create(records=[{description="Gold", address="7FF6A1DF1A30", variableType=2, value="999"}])
record_create(records=[{description="Player", address="0", variableType=14}])
record_create(records=[{description="Ammo", address="+4C8", variableType=2, parentId=<playerId>}])
record_create(records=[{description="Name", address="[game.exe+2F1A30]+60", variableType=6, unicode=true, length=32}])
record_create(records=[{description="Code bytes", address="game.exe+4C10", variableType=8, length=5}])
```

- A `parentId` must already exist, so one call cannot nest a record under a record it creates: create the group
  first, then the children in a second call.
- The parent is set before the value, so a `+offset` child's create value lands at the child's final address.
- If a step fails, the tool deletes the records this call created and returns the original error. If that rollback
  is incomplete, the error is `partial_effect` with the created and rolled-back counts: list the records first.
- The call is not idempotent. Check with `record_find` before creating, and after a timeout list before retrying.
- A type 11 record, or any `script`, needs Mcp:EnableAutoAssembler; the check runs before anything changes. A script
  holds at most 1048576 characters and 1048576 UTF-8 bytes (`limit_exceeded`).

## Pointer chains and relative addresses

- `offsets` makes `address` the base of a pointer chain: signed hex strings in dereference order, nearest the base
  first, as `pointer_read_chain` takes them and `pointer_list_paths` returns them. At most 128, each from -80000000 to
  7FFFFFFF; write a negative offset as `-8`, not `FFFFFFF8`. A bare `1224` is hex. `variableType` is the final value's
  type, and the value is written at the final address, never at the base.
- The float at `[[game.exe+1A2B30]+10]+4C8`:
  `record_create(records=[{description="Health", address="game.exe+1A2B30", variableType=4, offsets=["10", "4C8"]}])`;
  `pointer_read_chain(base="game.exe+1A2B30", offsets=["10", "4C8"], valueType="float")` reads the same chain.
- Cheat Engine and the `.CT` XML store offsets reversed (the first `Offset` element is next to the value); the tools
  convert, so reverse offsets you copy from a `.CT` file by hand. See [pointers](pointers.md).
- Record reads return `offsets` in the same form and order (normalised: `FFFFFFF8` reads `-8`); a symbolic offset
  from a table is copied as text, never evaluated. Check `currentAddress` and `offsetCount`.
- `record_update(updates=[{id=<id>, offsets=[]}])` removes a chain. Cheat Engine clears the offsets when the address
  changes, so send both together:
  `record_update(updates=[{id=<id>, address="game.exe+1A2B38", offsets=["10", "4C8"]}])`. Auto Assembler records
  take no offsets.
- A frozen pointer record resolves its chain again at every freeze tick, so the freeze follows a moving object.
- An address starting with `+` or `-` is relative to the final address of the nearest ancestor whose address is
  neither empty nor `0` (so groups and scripts made with `"0"` are skipped). Make one base record that resolves to the
  object, through a chain or a symbol a script registers (valid only while that script is enabled), and add its fields
  as children (`+4C8`, `+4CC`): fixing the base repairs every field.

## Update records

`record_update(updates=[{id=<id>, description="Health (float)", variableType=4}])` changes 1 to 256 records in order.
Each update needs `id` and at least one of `description`, `address`, `value`, `variableType`, `offsets`, `length`
and `unicode`.

- Every item is checked and every record read before the first change, so an unknown id, offsets on an Auto
  Assembler record or a refused value leave the batch unchanged.
- Within one update the description, address and type change first, then length and encoding, then offsets, then
  the value, written at the final address. A value writes target memory: ask first.
- Omit `value` to keep it. The value rules of creation apply to the type after the update; an empty value is accepted
  only for a string (6), where it writes an empty string.
- A record whose Cheat Engine option `moRecursiveSetValue` passes its value on to a nested record of type 0 to 5 or 13
  also takes only a plain number (checked through at most 4096 nested records). Else update the nested records one
  at a time.
- There is no rollback after the first change: a later failure leaves the earlier updates applied. Read the records
  before sending the rest, and never repeat a call whose `hostEffect` is `started` or `unknown`.

## Activate, freeze and deactivate

`record_set_active(ids=[<id>], active=true)` activates 1 to 256 records;
`record_set_active(ids=[<id>], active=false)` deactivates them.

- A value record freezes: Cheat Engine reads its current value and rewrites it at every freeze tick (100 ms by
  default). To hold another value, write it first with `record_update`. A script record runs its `[ENABLE]` section,
  and deactivation runs `[DISABLE]`.
- Each result has `requestedActive`, `active` and `pending`. `pending` is not an error: an asynchronous activation is
  still running, so read the record later with `record_get` (a later failure is not reported).
- A record Cheat Engine refuses keeps its state and the call goes on: `failure` then gives `reason` and Cheat
  Engine's message as `text`. A `reason` such as `aob_not_found`, `assert_failed`, `syntax_error` or
  `auto_assembler_error` names the script's fault, `inaccessible` a frozen value it could not write, and
  `not_reported` a refused deactivation or an activation the record's own `OnActivate` stopped.
- Call-level failures: `host_refused` with `hostEffect` `unknown` or `started`, and `not_found`. Earlier records keep
  their new state: read them first.
- Game logic runs between freeze writes, so a one-shot kill or a death check can still win, and freezing a display
  copy changes nothing. For a robust effect, change the code that writes the value
  ([find a writer](../Workflows/find-writer.md), [NOP patch](../Workflows/nop-patch.md)).
- An Auto Assembler record needs Mcp:EnableAutoAssembler, and so does a record whose `moActivateChildrenAsWell`
  (activating) or `moDeactivateChildrenAsWell` (deactivating) option reaches one below it: list the children with
  `record_list(parentId=<id>)` first. Only that switch is checked: the script is not classified, so a `{$lua}` block
  or a `createthread` runs without the other switches. Review each script first
  ([review an Auto Assembler script](../Workflows/review-aa-script.md)).
- Script activations are not MCP patches (`asm_list_patches` and `runtime_release_resources` ignore them): deactivate
  what you activated, before releasing MCP patches ([clean up a session](../Workflows/cleanup-session.md)). After a
  failed deactivation the patch may remain; inspect the code.

## Script records

- Create one with
  `record_create(records=[{description="Infinite ammo", address="0", variableType=11, script="<[ENABLE] and [DISABLE] text>"}])`.
- `record_set_script(id=<id>, script="<new text>")` replaces the text of an inactive Auto Assembler record and needs
  Mcp:EnableAutoAssembler; storing does not run it. An active or activating record is refused with `invalid_state`
  (Cheat Engine would disable it with the new `[DISABLE]` and the old allocations): deactivate, replace, reactivate.
- Check a script with `asm_check(script="<text>")` before storing or activating it: it checks both sections
  (`failedSection`) and needs only Mcp:EnableAutoAssembler. It refuses as `unsupported` a script that needs another
  switch (`{$lua}`, `{$c}`, `luacall`, `loadlibrary`, `include`, `USEMONO`), uses `globalalloc` or writes `$` before
  anything but hex digits: review such a script by reading it. A pass does not prove the activation will work.
- Script text is saved in the table; a patch from `asm_apply` is a session lease that no table keeps. Syntax and
  `[DISABLE]` rules: [auto-assembler](auto-assembler.md), [x64 injection](x64-injection.md).

## Dropdown lists

A dropdown gives a record named values (`0:Off`, `1:On`) that the user picks in Cheat Engine. It changes the table,
not target memory, and needs no switch.

- `record_get(ids=[<id>], includeDropdown=true)` adds `dropdown`: `items` (`value`, `description`), `itemCount`,
  `truncated`, the options `disallowManualInput`, `descriptionOnly` and `displayAsListItem`, and `linkedTo` (the
  description of the record whose list it uses).
- `record_set_dropdown(id=<id>, items=[{value="0", description="Off"}, {value="1", description="On"}], disallowManualInput=true)`
  replaces the whole list; an omitted option keeps its setting. `record_set_dropdown(id=<id>, items=[])` removes the
  list and turns the three options off.
- A value has no colon and at most 1024 characters; non-empty values must differ, ignoring ASCII case; an empty value
  makes a description-only line. At most 1024 items and 262144 UTF-8 bytes.
- Values are compared with the displayed value text, so a hex-displayed record needs hex values. A `*` value is the
  text shown for any other value when all three options are on.
- An Auto Assembler record or a linked record is refused with `invalid_state`: change the record `linkedTo` names.

## Organise and delete

- `record_group(description="Player", ids=[<id>, <id>])` creates a type 14 group record (`parentId` nests it) and
  moves the records under it one by one; if a later move fails, the group and earlier moves stay.
- `record_move(id=<id>, parentId=<groupId>)` moves one record with its children; `record_move(id=<id>)` moves it to
  the root. Any record can be a parent; Cheat Engine refuses a cycle. A moved `+offset` record resolves against its
  new ancestors.
- `record_delete(ids=[<id>])` deletes records with their children (`deleted` counts the listed ids). By Cheat
  Engine's source a delete does not run `[DISABLE]`: an active script's patch stays with no record left to undo it,
  so deactivate scripts, children included, first. An active listed script record needs Mcp:EnableAutoAssembler. A
  failure keeps the earlier deletions. Delete only what this session created or the user named.
- `record_clear()` deletes the whole list, children included (above 100000 records it refuses), and needs
  Mcp:EnableAutoAssembler when any record is an active script. `celua.txt` documents no `clear` method on the address
  list, which this tool calls: if it fails with `host_refused`, delete the top-level records with `record_delete`.
  There is no undo: save first, and clear only when the user asks.

## Load a table

- Table files must lie under a folder listed in `CheatEngineClient:AllowedTableRoots` in `appsettings.json` (empty by
  default, which refuses every load and save; a change applies after the plugin is disabled and re-enabled). This is
  not `Mcp:Files:AllowedRoots`, which governs other file writes. Paths are absolute, local and fixed and end in `.CT`,
  `.XML` or `.CETRAINER`; UNC, device and linked paths are refused. See [configuration](configuration.md).
- `table_list_files(offset=0, limit=100)` returns the `roots` and the table files below them (`path`, `size`,
  `lastWriteUtc`). `exact` is false when a root is missing, linked or unreadable, or past 10000 files.
- `table_load(path="<absolute path from table_list_files>", merge=false)` replaces the current address list, and
  `table_load(path="<absolute path>", merge=true)` adds to it. By Cheat Engine's source a replace removes every record
  without running any `[DISABLE]`, as a delete does, and also clears the table's comments, forms, Lua files and every
  global structure, MCP-made ones included: deactivate active scripts and save unsaved work first.
- Before Cheat Engine sees the file, the tool inspects it, keeps it open against writers until the load ends, and
  requires the switches its content reaches. A disabled switch fails with `capability_disabled` before anything runs.
  The scan also reads comments, so a `name (` in a comment can add switches:

| The table has | Required |
|---|---|
| a Lua script (it can run on load) or forms | Mcp:EnableUnsafeLua |
| Auto Assembler scripts | Mcp:EnableAutoAssembler, plus per script: `{$lua}` or `luacall` unsafe Lua; `{$c}`, `createthread` or `loadlibrary` target code execution; `{$luacode}`, an unknown `{$...}` directive, `include`, `loadbinary` or a command Cheat Engine does not build in (such as `USEMONO`) both; `kalloc` kernel access |
| `UsesMono` in the loaded or the current table, while a process is open | Mcp:EnableTargetCodeExecution |
| opaque content: a `.CETRAINER`, a protected, compressed or pre-5.6 binary `.CT`, an obfuscated, malformed or non-`CheatTable` XML, over 64 MiB | unsafe Lua, Auto Assembler and target code execution |

- The result reports the `inspection` (`format`, `requires`, counts and flags) and `monoAutoAttach`: when true, Cheat
  Engine starts its Mono data collector in the target after the load; tell the user
  ([Mono and .NET](mono-and-dotnet.md)).
- The switches control exposure, not safety: a table's Lua runs with Cheat Engine's full rights. A `.CETRAINER`
  always runs its Lua and always replaces the list, whatever `table_load.merge` says (the result's `merge` repeats
  the request). Load only tables the user trusts.
- The call may wait on a Cheat Engine dialog (by default Cheat Engine asks before running an unsigned table's Lua):
  tell the user. After a timeout, do not repeat the load; check `record_list`. A native failure is `host_refused`
  with `hostEffect` `started`: part of the table and its scripts may be in place.
- Loading does not reactivate records (Cheat Engine never reads a saved activation state), but the table's Lua can.
  After a load, list the records again and review each script (`record_get`, then `asm_check` or reading) before
  activating it.

## Save a table

- `table_save(path="<absolute path under a table root>", overwrite=false)` saves the whole current table (records,
  scripts, code list, user-defined symbols, structures, comments, forms, table Lua) as unprotected XML, even under a
  `.CETRAINER` name. Prefer `.CT`. It needs no switch but may wait on a Cheat Engine dialog.
- An existing file fails with `invalid_argument` unless `table_save.overwrite` is true; ask before overwriting
  (`replacedExisting` reports it). A failed save can leave a partial file: never overwrite the only copy.
- Raw heap addresses are saved as typed and break in the next session.
- After a Mono attach, Cheat Engine sets the table's `UsesMono` option by default, so the saved table carries it: its
  later load, and a process open while it is set (unless Cheat Engine ignores the option), need
  Mcp:EnableTargetCodeExecution.

## Build a robust table

1. Change code through AOB injection scripts (`aobscanmodule`, `registersymbol`), not hard-coded code addresses:
   `asm_generate_injection` builds one from an `aob_generate_signature` result (pass its offset as
   `asm_generate_injection.offset`; a module over 64 MiB gets a managed signature with offset 0). See
   [AOB signatures](aob-signatures.md).
2. Reach values through module-relative chains (`offsets`) or registered symbols, with the fields as `+offset`
   children of one base record. In a Unity Mono game with the collector attached, Cheat Engine also resolves
   `Class:field` and `Namespace.Class:Method` names, and the table then needs the collector at every load
   ([Mono and .NET](mono-and-dotnet.md)).
3. Make every `[DISABLE]` restore the original bytes, `unregistersymbol` each symbol and `dealloc` each allocation.
4. Enable and disable each script twice, then confirm the restored code: the same
   `memory_hash(address="<patched address>", size=16, algorithm="sha256")` before and after, `code_disassemble` or
   `module_find_patches`.
5. Restart the game, attach again, load another save or level, and verify every record.
6. Note the build (the PE time stamp in `module_get` output) in a record description, so a later failure is
   recognised as a game update. Ask the user to save their game before testing injections. Named values can go in a
   dropdown list.

Workflows: [build a robust table](../Workflows/build-robust-table.md),
[freeze a value](../Workflows/freeze-value.md), [manual pointer chain](../Workflows/manual-pointer-chain.md),
[pointer scan](../Workflows/pointer-scan.md), [repair after an update](../Workflows/repair-after-update.md),
[session report](../Workflows/session-report.md).

## Sources

- https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Cheat_Tables
- https://wiki.cheatengine.org/index.php?title=Help_File:General_settings
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/MemoryRecordUnit.pas (types, values,
  offsets, relative addresses, freeze, activation, recursive values, delete, table XML)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/addresslist.pas (delete and clear)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/CEFuncProc.pas (type names in tables)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/OpenSave.pas (table formats, replace load,
  Lua prompt)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/LuaMemoryRecord.pas (script setter)
- Cheat Engine 7.7: `celua.txt` (MemoryRecord, dropdowns, OnActivationFailure, Addresslist), `defines.lua` (`vt`
  codes), `autorun/monoscript.lua` (`UsesMono`, Mono symbols)
