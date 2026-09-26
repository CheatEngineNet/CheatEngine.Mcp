---
name: cheatengine-mcp
description: Operate one or more Cheat Engine instances through CheatEngine.Mcp for memory inspection, scans, address lists, freezing, speedhack, and debugging.
---

# CheatEngine.Mcp

Use the configured CheatEngine.Mcp gateway. Each Cheat Engine process loads its own plugin; one gateway connection routes calls to those instances. This guide does not install or launch Cheat Engine.

1. Call `list_instances`. Match the user's task to the returned name and Cheat Engine process ID. Names can repeat; ask which instance only when the intended one remains ambiguous.
2. Retain its exact `instanceId`. Supply it to every other tool, including inspection and cleanup. There is no global selected instance and no implicit default, even with only one CE running.
3. Call `get_runtime_info`, `get_plugin_version`, and `get_current_process` on that instance. The CE process ID from discovery differs from the attached target process ID.
4. Inspect capability evidence. A tool may be registered while its Client operation is unavailable in this host. Use [tool-catalog.md](references/tool-catalog.md) to find tools, then live schemas for exact arguments.
5. Select the authorized target process in that instance before target operations. Keep the instance ID with every saved resource ID or scan name.

If discovery reports `discoveryIncomplete: true`, its time budget expired; retry discovery before treating a missing instance as absent. Otherwise, an empty list means no responsive plugin was found under the same user and instance directory as the gateway. Do not guess a port or enable another plugin. When an instance disappears, report its unavailability; never redirect its work to another instance. Disable/re-enable or CE restart creates a new ID. Refresh discovery and inspect the new instance before continuing; old handles are invalid.

Keep reads and scans bounded. Each plugin uses its own activation-scoped Client, and every compound operation dispatches through that CE's main-thread boundary. Scans, allocations, patches, address records, structures, captures, and traces belong to their originating instance; identical names in two instances do not connect them. Two CE instances attached to the same target can still affect the same target memory.

Before changing processes, release owned state with `release_target_resources`. Cleanup proceeds in reverse creation order and stops at an incomplete outcome. Respect retryable/manual-recovery details; a failed cleanup must not be treated as successful. Changing targets in Cheat Engine itself may leave resources requiring manual recovery.

Read [scanning-and-debugging.md](references/scanning-and-debugging.md) for value comparisons, pointer snapshots/rescans, write/access captures, single-step traces, and register editing. Stop active debugger jobs before target changes. Pointer snapshots and paths are bounded managed copies; debugger callbacks are CE-owned Lua with expiry and explicit cleanup.

Memory writes, allocation, table changes, patches, and Lua can alter the target or host. Stay within the user's authorized target and operation. Table loading may execute Lua and needs a configured allowed root.

JSON settings are loaded on enable from the plugin folder and %APPDATA%/CheatEngine.Mcp/appsettings.json. MCP_DATA_DIRECTORY selects a separate absolute directory for user settings and logs; MCP_INSTANCE_NAME supplies a display label. Backends use authenticated loopback HTTP on automatic ports; the AI connects through the stdio gateway. MCP_INSTANCE_DIRECTORY must agree between gateway and plugins when overridden. Do not read or expose registry access tokens. Arbitrary execute_lua and Client Auto Assembler patches are disabled by default; dedicated mutation tools remain available. Do not change policy simply to bypass a rejected call.

Read [lua-execution.md](references/lua-execution.md) before using `execute_lua`. Prefer Client-backed tools. Additional dedicated debugger, DBVM, injection, structures, symbols, and code-analysis tools use fixed Lua bindings inside the Client operation boundary. Inspect errors and hostEffect before retrying. Lua-created host state can outlive plugin disable: remove breakpoints/structures explicitly and restore paused/speedhack state.

For address-list add/update, freeze/unfreeze, and speed changes, read [address-list-and-speedhack.md](references/address-list-and-speedhack.md). These affect CE-owned state and need explicit restoration within the same instance.

The gateway and plugin ship together as a complete folder with the native bridge and managed dependencies. Verify the loaded path/version before interpreting live results. A transport error after a mutation leaves the outcome uncertain; inspect state on the same instance before retrying.
