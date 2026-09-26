# Address lists, freezing, and speedhack

Pass the same discovered `instanceId` to every call in a workflow. Check `get_current_process` before changing target state. Examples below use `ceId` for that exact ID; resolve enum spellings from the live schema.

## Add a record and freeze its value

1. Inspect `get_address_list(instanceId=ceId, maximumRecords=256)` to avoid duplicating an existing record. The result lists top-level records; it is not a recursive table traversal.
2. Call `add_memory_record(instanceId=ceId, description="Health", address="game.exe+10", value="100", variableType="Dword")` using the authorized address and type. Setting the initial value can write target memory.
3. Retain `record.id` together with `ceId`. `update_memory_record(instanceId=ceId, id=recordId, value="100")` changes an existing record; it does not enable freezing.
4. For an ordinary numeric record, call `set_memory_record_active(instanceId=ceId, id=recordId, active=true)` to freeze. Inspect the returned record and target value. Activating an Auto Assembler/script record can execute its script instead; inspect the record type first.
5. Call `set_memory_record_active(instanceId=ceId, id=recordId, active=false)` to unfreeze. Delete only records the task owns or the user asked to remove with `delete_memory_record`.

Client-issued record IDs are not indices and must not be reused in another instance or after re-enable. CE's address list is host-owned: plugin disable does not guarantee its records or freezes disappear. `release_target_resources` handles Client leases; it does not replace address-list cleanup.

## Change target speed

Read `get_speedhack_speed(instanceId=ceId)` and retain the prior `result.speed`. Call `set_speedhack_speed(instanceId=ceId, speed=0.5)` for half speed or `speed=2` for double speed. The multiplier must be finite and positive; zero is not a pause command. Restore the previous multiplier when temporary work ends; `speed=1` requests normal speed when that is the intended state.

Readback reports CE's configured multiplier, not a measured guarantee about target timing. These dedicated tools use fixed Lua bindings and do not require enabling arbitrary `execute_lua`. If the host reports failure or a request loses its connection, inspect the same instance's state before retrying; the change may already have happened. Another CE attached to the same target can also affect that target.
