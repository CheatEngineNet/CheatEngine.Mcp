# Stable v2.0.0 acceptance criteria

Status: Planned criteria that have not yet passed.

Implementation, offline verification, native qualification, soak, and release publication are separate evidence levels.
A tool implementation or portable test result does not satisfy a native or release requirement.

## Evidence levels

- `implementation` proves that reviewed source and documentation implement the intended bounded contract.
- `V` proves locked restore, Debug build, and portable tests without LiveQualification or NativeLua.
- `V+` adds style and whitespace verification, Release build, `eng/Publish.ps1`, and review of intended golden changes.
- `VN` proves changed fixed Lua, Lua runtime, job kernel, or status-indicator behavior against a real Lua 5.3 x64 DLL with CE APIs stubbed.
- `AOT` proves Native AOT publication and the executable MCP smoke check for the exact gateway candidate.
- `VL` proves maintainer-authorized behavior in private real Cheat Engine copies against owned disposable targets.
- `soak` proves sustained candidate behavior and bounded resource recovery over the planned duration.
- `release` proves the final public package, tag, assets, hashes, installation, and first-use path.

The portable suite must pass against both Debug and Release outputs for release sign-off.
VN evidence must record the selected Lua DLL path in redacted form, its version when available, and its SHA-256.
VL remains outside ordinary CI and must follow the existing explicit acknowledgement, private-copy, user-state backup, reviewed-autorun, and disposable-target restrictions.
Kernel, hypervisor, debugger injection, compiler, and managed invocation probes require their separately reviewed qualification policy before VL execution.

## P1 pointer access analysis

The planned tool name is `pointer_get_access_info`, subject to final contract review.
The tool accepts bounded supplied instruction facts, target architecture, register values, and an optional observed access address.
The authoritative instruction representation and contradiction rules must be explicit in the public schema.
The result reports the memory operand, base and index registers, scale, signed displacement, effective address, candidate structure base, next pointer-search value, and uncertainty.
The evaluator must preserve the existing `debugger_start_capture` effective-address behavior by reusing or factoring its operand rules.
The tool must distinguish pre-execution context from post-execution access or write context.
The tool must handle x86 and x64 width, register aliases, address-size override, RIP-relative next-instruction semantics, signed offsets, and documented overflow or wrap behavior.
The tool must describe indexed array access as dynamic and must not present it as a stable pointer-chain offset.
The tool must return typed unsupported, missing-fact, contradictory-fact, unresolved-symbol, and observed-address-mismatch outcomes without inventing an address.
The supplied-facts path must perform no live target read, symbol lookup, mutation, or hidden target selection.
Portable and VN coverage must include aliases, scaling, positive and negative displacement, RIP-relative length, address-size override, supplied symbols, missing symbols, missing registers, multiple operands, implicit operands, segment bases, VSIB, undecodable input, and mismatch reporting.
Native qualification must prove that the fact shape captured from reviewed x86 and x64 fixture instructions produces the expected analysis without changing capture behavior.

## P1 batch pointer-chain validation

The planned tool name is `pointer_read_chains`, subject to final contract review.
One call accepts at most 128 candidates, at most 64 offsets per candidate, and at most 4,096 combined pointer and final-value reads.
Each dispatch slice admits at most 32 candidates and 128 reads, and combined final-value reads are limited to 65,536 bytes.
Each candidate has a caller-supplied stable identifier, a base expression, and offsets in the existing dereference-then-add signed hexadecimal convention.
The result keeps chain resolution, optional target-address comparison, and optional final-value read as separate statuses.
An unreadable hop must remain distinct from a resolved miss, a resolved match, and a failed final-value read.
The tool compares the resolved address before the optional final-value read and preserves the comparison result when only that read fails.
Matches-only mode must retain aggregate processed, matched, missed, unreadable, error, and not-run counts.
Cancellation or interruption after completed candidates must return bounded partial progress without describing unprocessed candidates as misses.
Every candidate in one call must use one target selection epoch, and a changed epoch must stop the remaining work.
Module expressions must rebase at validation time, while absolute roots must be documented as restart-sensitive.
The tool must not create, mutate, or delete saved pointer maps, scans, paths, or caller input.
Portable coverage must verify bounds, stable ordering, all statuses, overflow, signed offsets, module rebasing, response summaries, cancellation, and target-epoch change.
Native qualification must exercise known stable and unstable chains on disposable x86 and x64 fixtures across target restarts, including an unreadable hop and a resolved address with an unreadable final typed value.

## P1 typed C# compilation

The planned tool name is `exec_compile_csharp`, subject to feasibility and final contract review.
The feasibility check must confirm `compileCS` availability, signature, diagnostics, prerequisites, output filename, and lifetime on the reviewed Cheat Engine 7.7 build.
The tool accepts at most 131,072 source characters and at most 32 explicit reference assemblies.
Every reference must pass the existing host-file policy and remain held against replacement while Cheat Engine reads it.
The tool requires the target-code-execution capability before invoking Cheat Engine.
The operation uses the blocking-native dispatch class and reports timeout or lost-response effects honestly.
The stable path must copy the generated assembly to a caller-selected approved-root path before returning success.
The result must include the checked output path, byte length, SHA-256, bounded diagnostics, and the compiler availability outcome.
Compiler diagnostic text is limited to 16,384 UTF-8 bytes with an explicit truncation flag.
The upstream compiler supplies line-message text rather than reliable structured diagnostic codes or severities.
Compilation remains separate from injection or invocation, and a later `exec_inject_dotnet` call is always explicit.
The implementation must not promise assembly unload, CE temporary-file retention, or cleanup that Cheat Engine cannot confirm.
Portable and VN coverage must include bounds, disabled capability, invalid references, changing files, protected paths, diagnostics, missing compiler, output collision, copy failure, and missing or expired CE output.
Native qualification requires a separately reviewed code-execution policy and must cover valid source, compiler diagnostics, unavailable prerequisites, instance-B shutdown during an instance-A artifact workflow, approved-root export, separate injection, and cleanup.
Failure of the feasibility check blocks this feature from contract freeze and requires an explicit linked deferral with evidence.

## Contract and package acceptance

The reviewed tool inventory, registrations, source-generated JSON metadata, backend and gateway schemas, resources, prompts, completions, knowledge documents, and packaged reflection output must agree.
Reflection-disabled JSON and Native AOT analysis must cover every new input and result type.
Exact integers, 64-bit addresses, signed offsets, null values, empty values, errors, host effects, retryability, details, hints, and correlation identifiers must survive backend and gateway serialization.
Every routed backend call must require its explicit `instanceId`, and disappearance must never select another instance.
The packaged-DLL probe must receive the reviewed golden directory explicitly and compare the actual lone packaged plugin DLL with the candidate goldens.
The plugin DLL, gateway executable, ZIP, checksums, version, source identity, and tag must agree before publication.

## Performance acceptance

Performance thresholds remain planned until three reproducible baseline runs record the environment, workload sizes, and measurement method.
For a short operation, the candidate p95 must not exceed the greater of twice the baseline p95 or the baseline p95 plus 100 milliseconds.
For a bounded batch or job operation, the candidate p95 must not exceed one and one-half times the baseline p95.
Instance discovery, busy refusal, and admission failure must remain bounded and must not wait for unrelated native work to finish.
Long work must expose bounded jobs, progress, truncation, dropped-item accounting, stop behavior, and TTL behavior.
Documented blocking-native calls may outlive an MCP timeout, but they must report that limitation and must not report false success.
Completions and live resources must share admission bounds and must not start an unbounded scan.

## Planned two-hour soak

The soak is planned and has not passed.
The exact candidate package must run for two continuous hours with two private Cheat Engine instances.
The workload must include at least 1,000 short calls, 20 bounded jobs, 30 plugin enable or disable cycles, and 10 target restart or switch cycles.
The soak fails on a crash, hang, cross-instance effect, stale target effect, cleanup failure, unexplained drop, false success, or retained owned resource.
After final cleanup, process handles must return to no more than the stabilized baseline plus 25.
After final cleanup, private bytes must return to no more than the stabilized baseline plus 64 MiB.
The baseline and final measurements must use the same idle interval and collection method.
Any threshold change requires recorded baseline evidence and review before RC freeze.

## Evidence record schema

Each evidence entry in [release-signoff.md](release-signoff.md) must include a unique evidence identifier and the roadmap requirement identifiers it covers.
Each entry must name its level, status, candidate version, full commit, clean-tree state, configuration, and applicable artifact hashes.
Each native entry must record the Windows build, Cheat Engine version and installation fingerprint, exact .NET runtimes, target architecture, fixture identity, and MCP client when applicable.
Each entry must record the start time, duration, bounded cases, expected result, actual result, cleanup outcome, limitations, reviewer, and safe evidence reference.
An entry status is `pass`, `fail`, `blocked`, `not_run`, or `not_applicable`.
Raw tokens, discovery records, personal paths, e-mail addresses, and unredacted live transcripts must never enter committed evidence.
A roadmap checkbox may close only when reviewed evidence covers its complete acceptance criterion on the applicable candidate or links an explicit reviewed deferral.

## Installation and migration acceptance

Clean installation must use only the exact candidate ZIP and its included instructions.
Upgrade qualification must replace beta.2 and any older folder deployment without mixing plugin and gateway builds.
Install, upgrade, disable, uninstall, and rollback must preserve user configuration, tables, unrelated plugins, Cheat Engine settings, and recoverable backups.
Paths with spaces, a standard user account, allowed roots, read-only deployment behavior, malformed configuration, occupied endpoints, and unavailable log locations must have recorded outcomes.
The final release notes must state the support matrix, beta migration, capability limitations, recovery paths, and changes since beta.2.
