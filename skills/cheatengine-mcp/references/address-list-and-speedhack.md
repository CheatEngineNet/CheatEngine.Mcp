# Address lists, freezing, and speedhack

Pass the same discovered `instanceId` to every call in a workflow. Check `process_get_current` before changing target state. Examples below use `ceId` for that exact ID; resolve enum spellings from the live schema.

## Add a record and freeze its value

1. Inspect `record_list(instanceId=ceId)` to avoid duplicating an existing record. The result lists top-level records; it is not a recursive table traversal.
2. Call `record_create` with the authorized description, address, type, and initial value. Setting the initial value can write target memory.
3. Retain `record.id` together with `ceId`. `record_update` changes an existing record; it does not enable freezing.
4. For an ordinary numeric record, call `record_set_active(ids=[recordId], active=true)` to freeze. Inspect the returned record and target value. Activating an Auto Assembler/script record can execute its script instead; inspect the record type first.
5. Call `record_set_active(ids=[recordId], active=false)` to unfreeze. Delete only records the task owns or the user asked to remove with `record_delete`.

Client-issued record IDs are not indices and must not be reused in another instance or after re-enable. CE's address list is host-owned: plugin disable does not guarantee its records or freezes disappear. `runtime_release_resources` handles Client leases; it does not replace address-list cleanup.

## Change target speed

Read `speedhack_get_state(instanceId=ceId)` and retain the prior `speed`. Call `speedhack_set_speed(instanceId=ceId, speed=0.5)` for half speed or `speed=2` for double speed. The multiplier must be finite and positive; zero is not a pause command. Restore the previous multiplier when temporary work ends; `speed=1` requests normal speed when that is the intended state.

Readback reports CE's configured multiplier, not a measured guarantee about target timing. These dedicated tools use fixed Lua bindings and do not require enabling arbitrary `lua_execute`. If the host reports failure or a request loses its connection, inspect the same instance's state before retrying; the change may already have happened. Another CE attached to the same target can also affect that target.
