# Address list records and cheat tables

CE's address list holds records; a cheat table (`.CT`, XML) saves them together with scripts and structures. The address
list belongs to CE: plugin disable does not remove records or stop freezes, and `runtime_release_resources` does not
touch them.

## Record kinds

| `kind`          | What it is                                               | Activation does                                |
|-----------------|----------------------------------------------------------|------------------------------------------------|
| `value`         | an address or pointer chain plus a `valueType`           | freezes the value (periodic write)             |
| `script`        | an Auto Assembler script with `[ENABLE]` and `[DISABLE]` | runs `[ENABLE]`; deactivation runs `[DISABLE]` |
| `group`         | a header with no address                                 | groups children; may activate them             |
| `address_group` | a header that carries an address                         | children may use addresses relative to it      |

`valueType` maps to CE record types: `int8`/`uint8` Byte, `int16`/`uint16` 2 Bytes, `int32`/`uint32` 4 Bytes, `int64`/
`uint64` 8 Bytes, `float` Float, `double` Double, `pointer` 4 or 8 Bytes shown as hex, `string` and `wstring` String
(UTF-16 for `wstring`) with a length, `bytes` Array of byte with a length. See [value-scans](value-scans.md) for
choosing one.

## Addresses and offsets

- Use expressions that survive a restart: `game.exe+1A2B30`, a symbol a script registers (`playerBase`), or a pointer
  chain rooted in a module.
- `offsets` are signed hex strings in dereference order, the same order as `pointer_read_chain`. CE stores them reversed
  internally; the tools convert (see [pointers](pointers.md)).
- An address starting with `+` or `-` is relative to the nearest ancestor that has an address. Put `+4C8`, `+4CC` under
  an `address_group` or pointer record set to the object base, and one base change fixes every child.
- A raw heap address (no module, no symbol) is only valid until the object moves. Never save one in a table meant to be
  reused.

## Read

- `record_list(parentId?, depth)` pages the tree: `id`, `parentId`, `depth`, `description`, `kind`, `valueType`,
  `address`, `currentAddress`, `value`, `active`, `childCount`.
- `record_get(ids=[...], includeScript=true)` returns full details: offsets, display options, dropdown, hotkeys, script.
- `record_find(descriptionContains=..., addressExpression=..., valueType=..., active=...)` searches; give at least one
  filter.
- `record_get_selected` reads the user's selection in CE; `record_select(id)` changes it.

## Create and update

- `record_create(records=[...])` creates up to 256 records atomically. Each item has `kind` and `description`, plus
  `address`, `offsets`, `valueType`, `value`, `length`, `showAsHex`, `parentId` or `script` as needed.
- If an item fails, the earlier ones are deleted again and the error carries `details` (`failedIndex`, `createdIds`,
  `rolledBack`). If that rollback cannot be confirmed, the error is `partial_effect`; list the records before doing
  anything else.
- Passing `value` on create or update **writes target memory**.
- `record_update(updates=[{id, ...}])` changes description, address, type, value, offsets and display. It stops at the
  first failure with `partial_effect` (`appliedIds`, `failedIndex`).
- Batch related records in one call; it is far cheaper than one call per record. `record_create` rolls back a failed
  batch; `record_update` keeps the updates applied before the failure.

## Activate and freeze

- `record_set_active(ids=[...], active=true)` activates records. Each result reports `outcome` (`applied`, `unchanged`
  or `pending`) and `asyncProcessing`. `pending` or a rejected activation comes back as `partial_effect`: re-read the
  records instead of repeating the call.
- A freeze is a **periodic write** at CE's freeze interval. Game logic still runs between writes: a death check, a
  one-shot drop below zero or a server-side check can still win. For a robust effect, patch the code that changes the
  value (see [auto-assembler](auto-assembler.md)).
- Freezing a display copy changes nothing; freeze the address the game logic writes (see [value-scans](value-scans.md)).
- Activating a `script` record runs its `[ENABLE]` section in the target. It needs the `EnableAutoAssembler` gate, plus
  `EnableUnsafeLua` for `{$lua}` blocks and `EnableTargetCodeExecution` for `{$c}`, `createthread` or `loadlibrary`. A
  group set to activate its children applies the same checks to them.
- Unfreeze or disable with `active=false`. Deactivating a script runs `[DISABLE]`; if that fails, the patch may still be
  in place, so inspect the code with `code_disassemble`.

## Ids

- Record ids come from the Client and are scoped to this plugin activation **and** to the current table load. After
  `table_load`, every earlier id is stale and fails with `not_found`; list again.
- Ids never carry across instances, and a plugin disable and re-enable creates a new instance with new ids.
- Never reuse an id from memory after a gap; confirm it with `record_get` first.

## Groups and hierarchy

- `record_group(ids=[...], description="Player", addressGroup=false, address?, parentId?)` creates a header and moves
  the records under it, like CE's "Add to new group". A failure restores the original parents.
- `record_move(ids=[...], parentId=<header>)` re-parents records; omit `parentId` to move them to the root. A cycle is
  refused with `invalid_argument`.
- `record_delete(ids=[...])` deletes records and their children (`deletedWithAncestor` lists those). Delete only records
  this session created or the user asked to remove.
- `record_update` can set CE's record options, such as hiding children or activating and deactivating children with the
  parent. Use them to make a script header switch its dependent records.

## Script records

- Create with `record_create(records=[{kind:"script", description, script}])`, or replace the text with
  `record_set_script(id, script)`. Both require the `EnableAutoAssembler` gate.
- Check every script with `asm_check` first. It is not read-only: CE runs `{$lua}` blocks during the check.
- Put value records that use a script's symbols (`[playerBase]+4C8`) under that script. They resolve only while it is
  enabled.
- The script text is saved in the table. Patches applied with `asm_apply` are not: they are session resources released
  by `asm_release_patch`. Use script records for anything the user wants to keep.

## Robust tables

1. Change code through AOB injection scripts with `aobscanmodule` and `registersymbol`, never hard-coded code addresses
   (see [aob-signatures](aob-signatures.md)).
2. Reach values through module-relative pointer chains or through symbols registered by scripts.
3. Make every `[DISABLE]` restore the original bytes, `unregistersymbol` each symbol and `dealloc` each allocation.
4. Enable and disable each script twice in a row. The second cycle exposes missing restores, double allocations and
   leaked symbols. Confirm the code is restored with `code_disassemble` or `module_find_patches`.
5. Restart the game, load a different save or level, and re-verify every record and script.
6. Note the game version (`module_get` shows the PE timestamp) in a group description, so a later failure is recognised
   as an update.
7. Ask the user to save their game before testing injections; a bad script can crash the target.

## Load and save tables

- `table_list_files(root?, nameContains?)` lists the configured allowed roots and the table files in them.
- `table_load(path, merge=false, allowTableLua=false)` loads a table from an allowed root
  (`CheatEngineClient:AllowedTableRoots`). `merge=false` replaces the current address list, so save first if it holds
  unsaved work.
- A table can carry a Lua script that CE runs on load, scripts that change the target, and a request to attach CE's Mono
  collector. MCP inspects the file first: Lua requires `allowTableLua=true` and the `EnableUnsafeLua` gate; Mono use
  requires `EnableTargetCodeExecution`; script records require `EnableAutoAssembler`; trainers (`.CETRAINER`),
  compressed or unreadable tables are refused unless both `EnableUnsafeLua` and `EnableTargetCodeExecution` are on.
- The gates are exposure switches, not a sandbox. Load only tables the user trusts; a table's Lua runs with CE's full
  rights.
- Loading may raise a CE prompt on screen. Tell the user, and do not repeat the call if it times out; check
  `record_list` first.
- `table_save(path, overwrite=false)` saves records, scripts and CE's global structures under an allowed root. An
  existing file needs `overwrite=true`.
- A path outside the roots fails with `invalid_argument` and the roots in `hint`. Changing the roots is the user's
  configuration decision.

## Sources

- https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Cheat_Tables
- https://wiki.cheatengine.org/index.php?title=Tutorials:Create_cheat_table_full:health_hack
- https://wiki.cheatengine.org/index.php?title=Help_File:General_settings
- https://wiki.cheatengine.org/index.php?title=Auto_Assembler:registerSymbol
- https://wiki.cheatengine.org/index.php?title=Tutorials:AOBs
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/bin/celua.txt
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/MemoryRecordUnit.pas
