# Symbol-source qualification

Status: **open**. `symbol_enable_sources` has portable and real-Lua adapter coverage, but no qualifying native
success receipt. The returned source flags describe the completed requests; they are not independent evidence of
loaded PDBs or observed network traffic.

## Windows source

The audited CE implementation of `enableWindowsSymbols()` sets a search path ending in
`https://msdl.microsoft.com/download/symbols` and reinitializes symbols. A local `_NT_SYMBOL_PATH` becomes its cache
prefix; it does not remove the server fallback. The Lua helper exposes no local-only path argument. Do not execute
this case on an ordinary connected host merely to close the qualification row.

A qualifying run needs an independently verified network-isolated private CE process, isolation in force before
startup, and an exact matching image/PDB already available locally. Freeze the host, product, fixture and PDB
identities before the run. Do not change machine firewall, driver, boot or security configuration as part of an
ordinary qualification run.

Use the owned native-symbol DLL/PDB fixture as an effect oracle: generic symbol reinitialization visits every loaded
process module, so the fixture is eligible. Independently verify its debug-directory/PDB identity, show that its PDB
structure layout was unavailable before source enabling, and read back all four known field offsets and types afterward.
Do not call `symbol_add_module` or register that symbol before the check; either would provide a different cause for
success. A cache hit or the requested-state response alone is insufficient. Record network isolation, source-enabling
and readback receipts, restart behavior, restored user state and unchanged installed files.

## Kernel source

`enableKernelSymbols()` turns on kernel-symbol enumeration and reinitializes the symbol handler. Its implementation
does not initialize DBK or DBVM, but later PDB loads can consult the current DbgHelp search path. A kernel-only call
therefore still needs controlled symbol paths and independently verified network isolation or a qualified local-only
API. Compare the loaded-driver image/base/export view against independent Windows and PE observations, then confirm
that the driver inventory is unchanged. This qualifies kernel-symbol loading only; it cannot qualify any DBVM tool.

## Current repairs and evidence boundary

The local adapter checks every requested API before starting either source. Missing APIs return
`unsupported/not_started`; a kernel-only call that throws reports an unknown effect without claiming Windows
symbols were enabled. A kernel failure after a successful Windows call retains its partial-effect report.

These regressions use real Lua with CE APIs simulated. They do not close this native gate. Keep
`nativeQualificationComplete=false` until both applicable source branches have matching success, effect-readback and
cleanup receipts against the frozen release candidate.

## Prepared isolated Windows runner

The `symbol-sources` scenario is implemented but has no native success receipt. It selects `windows=true, kernel=false`.
The ordinary live acknowledgement and the following exact acknowledgements are required; setting them is not a substitute
for the maintainer's explicit approval of the host-policy change:

| Variable | Exact value |
| --- | --- |
| `CHEATENGINE_MCP_LIVE_SYMBOL_FIXTURE_QUALIFICATION` | `I_AUTHORIZE_FIXED_NATIVE_DLL_AND_LOCAL_PDB_ON_PRIVATE_CE_AND_OWNED_TARGETS` |
| `CHEATENGINE_MCP_LIVE_SYMBOL_SOURCES_QUALIFICATION` | `I_AUTHORIZE_ISOLATED_WINDOWS_SYMBOL_SOURCE_NETWORK_PROBES_ON_PRIVATE_CE` |
| `CHEATENGINE_MCP_LIVE_WFP_ISOLATION_QUALIFICATION` | `I_AUTHORIZE_DYNAMIC_WFP_ISOLATION_FOR_PRIVATE_CE_SOURCE_QUALIFICATION` |

An elevated launcher owns dynamic WFP sessions with two block filters per private CE executable: IPv4 and IPv6
non-loopback connects, bound to the pinned image path and hash. There are no permit filters. Existing loopback policy
still applies and the real gateway must connect successfully. Filters are installed and read back before CE starts;
suspended creation assigns CE to an owned kill-on-close job before its first thread resumes. CE-local fixed probes must
return `WSAEACCES` for both documentation-address families before and after source enabling. A timeout, routing failure
or requested source flag cannot satisfy that oracle.

The audited CE helper appends the literal `Cheat Engine Symbols` to `_NT_SYMBOL_PATH` before building its server path.
The child environment therefore supplies the owned run root with a trailing separator. The matching PDB is staged under
`<run-root>/Cheat Engine Symbols/<pdb-name>/<GUID+age>/<pdb-name>`, and its original location is hidden in an owned backup.
Both alternate symbol-path variables are removed from the child environment. A saved CE `Don't use tempdir` preference
refuses admission, since that preference would select another cache root.

Cleanup stops the owned targets and CE jobs before removing policy, then checks filter and sublayer absence using a
separate WFP observer and restores the original PDB with a hash check. A failed critical cleanup retains ownership and
the run lock for retry. No ordinary portable or NativeLua test activates WFP or calls the socket probes.

## Audited source

- [CE Lua helpers](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaHandler.pas)
- [CE Windows-source setup](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/CEFuncProc.pas)
- [CE module and driver symbol loading](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/symbolhandler.pas)
