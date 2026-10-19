# Additional native qualification cases

These scenarios implement the maintainer's request to test every advertised tool against owned fixtures.
Preparing a case does not qualify it: the full session must pass, restore user state, preserve the installed
Cheat Engine files and verify cleanup before its evidence can be checked off in [the checklist](README.md).
Only one private session runs at a time because the registry and application-data backup is shared.

## Ordinary workflows

All four optional gates remain disabled. The additional cases release one owned allocation, stop an independent
bounded scanner, infer a structure from known bytes, and open/save a fixed private file as a process target.
The file case checks CE's exact file-target sentinel, compares the complete saved output against independent input
bytes, reattaches the original owned process and removes the two private files. It does not treat a file as an
operating-system process or use local-process memory tools against it. The x86 module-export oracle reads the physical SysWOW64 file;
its base address, PE machine and exports are checked independently of the product parser.

## Fixed Lua and assembler

Select `CHEATENGINE_MCP_LIVE_QUALIFICATION_SCENARIO=assembler` and set
`CHEATENGINE_MCP_LIVE_ASSEMBLER_QUALIFICATION` to
`I_AUTHORIZE_FIXED_LUA_AND_REVERSIBLE_PATCHES_ON_OWNED_ALLOCATIONS`, together with the normal live acknowledgement.
Only the private copies enable unsafe Lua and Auto Assembler. Target-code execution and kernel access remain disabled.
The Lua input returns fixed arithmetic and text values. Patches affect only a named allocation that is never executed;
the cases verify changed bytes, rollback bytes and tracked-patch removal. An inactive address-list script has its
exact text restored before deletion. Generated scripts are checked as generated artifacts, separately from applied
patches. Any failed rollback fails the session and prevents further automatic native runs.

## Windows debugger

Select `CHEATENGINE_MCP_LIVE_QUALIFICATION_SCENARIO=debugger` and set
`CHEATENGINE_MCP_LIVE_DEBUGGER_QUALIFICATION` to
`I_AUTHORIZE_WINDOWS_DEBUGGER_CHECKS_ON_OWNED_DISPOSABLE_TARGETS`, together with the normal live acknowledgement.
All four optional gates remain disabled. The fixture uses the Windows debugger on the owned target only, with
bounded waits and independently observed process/thread identity. It verifies stopped state and context, then
continues and detaches before the target is stopped. This initial fixture does not establish coverage for trace,
capture, run-to or stepping against a known function.

## Existing compiler and managed-injection cases

The fixed compiler and Framework-target cases retain their separate acknowledgements, source hashes, clean-commit
checks, temporary directories and held-file validation. Fresh x86 and x64 fixtures must be built from the current
`eng/LiveFrameworkTarget/Program.cs`; old fixture identities are not reused after a source change. The generated
assembly's only invoked method returns the fixed value `7319`. This is separate evidence from compiler output.

The maintainer's current all-tools request includes these fixed cases on owned disposable targets. Historical
qualification policies retain their original decisions and evidence; their old "pending approval" status is not
a fresh restriction on this session's explicitly requested work. New inputs and cleanup are reviewed before running.
No drivers, runtimes, compilers or dependencies are installed or changed by these sessions.
