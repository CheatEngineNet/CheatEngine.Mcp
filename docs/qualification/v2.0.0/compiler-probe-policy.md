# Native compileCS qualification policy

Status: The maintainer approved compiler-only phases 1 through 4 on 2026-10-08 for this roadmap session.
Optional phase 5 (managed injection) is not approved.

[CONTRIBUTING.md](../../../CONTRIBUTING.md#live-qualification) currently prohibits live code-execution probes.
This approval is a bounded exception and does not change the general rule.
Every compiler run still requires both explicit runner acknowledgements, a reviewed clean candidate, and the prerequisites below.
Ordinary VL authorization alone is insufficient for a future compiler session; managed injection requires separate approval.

## Decision and evidence boundary

The purpose is to qualify Cheat Engine's `compileCS` availability, bounded diagnostics, immediate approved-root export, and observed temporary-output lifetime.
The compiler phase never accepts caller-authored or operator-edited source.
The compiler phase uses only the fixed payloads in this document and records their exact UTF-8 byte hashes before launch.
The compiler phase does not inject, invoke, debug, patch, allocate target memory, use a kernel driver, use DBVM, or contact a non-loopback network endpoint.
Compilation and export evidence does not qualify managed injection or execution.
An injection phase is a separate exception within this policy and remains disabled unless its additional approval and fixture prerequisites are satisfied.

## Required authorization

The runner must continue to require the existing live acknowledgement and every existing VL safety check.
The runner must also require a second exact acknowledgement dedicated to this exception.
The variable is `CHEATENGINE_MCP_LIVE_CODE_EXECUTION_QUALIFICATION`.
The value is `I_AUTHORIZE_FIXED_COMPILECS_PROBES_ON_PRIVATE_CE_AND_OWNED_TARGETS`.
The runner must also select `CHEATENGINE_MCP_LIVE_QUALIFICATION_SCENARIO=compiler`; ordinary smoke qualification remains the default.
The second acknowledgement must be absent by default, refused in CI, and evaluated before a CE process, target, run directory, or user-state backup is created.
The run report must record that both acknowledgements were present without recording their environment blocks or any discovery token.
Approval applies to one reviewed candidate commit, package hash, policy revision, fixed payload hash set, and operator session.
Changing any of those facts requires a new review before another run.

## Fixed compiler payloads

The valid payload is the following exact source with UTF-8 encoding and LF line endings inside the compiler argument.
It has no trailing newline.

```csharp
public static class CheatEngineMcpCompileCsProbe
{
    public static int Run(string value)
    {
        return value == "CEMCP-COMPILECS-PROBE-V1" ? 7319 : -7319;
    }
}
```

The payload has no file, environment, process, thread, reflection, native interop, unsafe, debugger, network, or target-memory operation.
The fixed invocation parameter is `CEMCP-COMPILECS-PROBE-V1`, and the only successful method result is decimal `7319`.
The invalid-source payload is the exact ASCII text `public static class CheatEngineMcpCompileCsProbe {`.
The invalid-reference fixture contains the exact ASCII bytes `CEMCP-NOT-AN-ASSEMBLY`.
No operator prompt, issue text, file content, or MCP argument may replace or append to these payloads.
The runner must compare the runtime payload hashes with the reviewed expected hashes before starting either CE host.

## Prerequisites

- The candidate checkout is clean and identifies one full commit.
- The exact Release distribution has passed V+, applicable VN, AOT, and packaged-DLL golden comparison before this exception run.
- The report records the candidate version and SHA-256 of the plugin DLL, gateway executable, and distribution ZIP when available.
- The installed Cheat Engine is an accepted x64 7.7 host and passes the existing dynamic installation fingerprint and reviewed-autorun checks.
- The report records the exact Cheat Engine version, executable hash, Windows build, and installed .NET runtime versions.
- Every ordinary Cheat Engine process, CE tutorial process, disposable target, and DebugView process is closed before admission.
- CI is false, and the run root remains outside the repository and outside the installed Cheat Engine directory.
- The existing registry and application-data backup guard is armed before either private CE copy starts.
- Private CE copies A and B use only the candidate plugin copy, one shared private instance registry, and the candidate gateway copy.
- Owned disposable managed fixtures exist for x86 and x64 and contain no input, window, network listener, persistent state, or unrelated workload.
- Each fixture publishes its owned process identity and bounded stop marker and exits on that marker or its deadline; the runtime required by `exec_inject_dotnet` is a separate prerequisite of optional phase 5.
- The compiler prerequisite inventory is read-only and records whether CE exposes `compileCS` and which compiler or framework dependency CE reports.
- No dependency, framework, compiler, certificate, registry value, driver, or runtime is installed or changed during the run.

## Run-owned paths and configuration

Each host receives a distinct `TEMP` and `TMP` directory below its live run directory before process start.
Refuse compiler admission when Cheat Engine's saved `Don't use tempdir` setting is enabled, because its `Scanfolder` value can override those environment variables.
Do not alter that setting to admit a run.
Host A uses `<run>/compiler/A/temp`, `<run>/compiler/A/references`, and `<run>/compiler/A/output`.
Host B uses `<run>/compiler/B/temp`, `<run>/compiler/B/references`, and `<run>/compiler/B/output`.
The fixed invalid-reference file is created only below host A's reference directory.
The plugin write policy lists only the two run-owned output directories required by the current phase.
Reference and core-assembly arguments, when used, must name reviewed files below the run-owned reference directories.
The instance registry, plugin data, CE copies, backups, and repository remain protected from tool file access.
`EnableUnsafeLua`, `EnableAutoAssembler`, and `EnableKernelAccess` are false for every phase.
`AllowedTableRoots` remains empty.
`EnableTargetCodeExecution` is false for the gate-refusal phase and true only after a fresh private-host activation for approved compiler phases.
Only the gateway's local stdio and authenticated loopback backend connections are permitted.

## Admission and baseline

1. Resolve both acknowledgements and every normal VL precondition before creating the run.
2. Record candidate, environment, payload hashes, path roots, configuration, and the requested target architecture.
3. Back up user state and fingerprint the source installation through the existing guard.
4. Create the two private CE copies, private plugin copies, gateway copy, fixture copy, and run-owned scratch directories.
5. Snapshot the run-owned temp, reference, and output trees as relative path, length, and SHA-256 entries.
6. Record CE A and B process identifiers, activation identities, instance identifiers, private bytes, handle counts, and runtime capability evidence.
7. Refuse the run if a path escapes the run directory, a payload hash differs, a private copy differs, or an unrelated CE-like process appears.

CE's scanner creates an exclusive four-byte PID lease at `Cheat Engine/{GUID}/inuse.lock` below each private temporary root.
When that exact path and length produce a Windows sharing violation, record its path, length, and exclusive-lease status with an unavailable hash; never claim its unreadable bytes were compared.
All other unreadable files still stop qualification, and this exception never applies to an assembly, output, or reference file.
Temporary-inventory comparisons cover readable file hashes and the path/length of these explicitly recorded leases.

## Phase 1: disabled gate before compiler admission

1. Start host A with `EnableTargetCodeExecution=false` and the other three effect gates false.
2. Call `exec_compile_csharp` with the fixed valid source, no references, no core assembly, and `<run>/compiler/A/output/gate-disabled.dll`.
3. Require `capability_disabled` with `hostEffect=not_started`.
4. Require no tool dispatch or fixed-Lua receipt for `exec_compile_csharp`.
5. Require no destination, protected partial file, raw compiler artifact, or run-owned temp-tree change.
6. Stop host A normally and verify its discovery record withdraws.
7. Stop the run immediately if the compiler is called, a file appears, or the result reports any started, partial, unknown, or cleanup-unconfirmed effect.

## Phase 2: successful compile and immediate export

1. Start fresh hosts A and B with `EnableTargetCodeExecution=true` and all other effect gates false.
2. Attach each private host only to its owned fixture of the selected architecture.
3. Call `exec_compile_csharp` through instance A with the fixed valid source, no references, no core assembly, `overwrite=false`, and `<run>/compiler/A/output/probe.dll`.
4. Require one successful result with an absolute anchored output path, a positive length no greater than 16 MiB, and an uppercase SHA-256.
5. Reopen the exported file read-only and require the reported length and SHA-256 to match the bytes on disk.
6. Require the exported file to begin with the PE `MZ` signature and require no protected partial file to remain.
7. Require instance B to remain responsive and require no file below B's temp, reference, or output directories to change.
8. Record compile-to-result duration, export duration when separately measurable, CE A private-byte and handle deltas, tool result, correlation identifier, and redacted file facts.
9. Do not treat a successful export as proof that CE's original temporary file remains available.

## Phase 3: invalid source and reference handling

1. Call instance A with the fixed invalid-source payload and `<run>/compiler/A/output/invalid-source.dll`.
2. Require a bounded compiler diagnostic, no published output, no protected partial file, and an honest non-success host effect.
3. Require diagnostic text to be at most 16,384 UTF-8 bytes and require the truncation flag to match the recorded byte count.
4. Call instance A with the fixed valid source and one nonexistent path below the reference root.
5. Require refusal before compiler dispatch with `hostEffect=not_started`, no output, and no temp-tree change.
6. Call instance A with the fixed valid source and the fixed invalid-reference file.
7. Require a bounded compiler or reference diagnostic, no published output, and no protected partial file.
8. Repeat the missing and invalid cases for `coreAssembly` only when the supported CE signature accepts that optional argument.
9. Stop the phase if CE loads or executes an invalid reference, publishes an output after a reported failure, or changes a file outside host A's run-owned roots.

Expected compiler diagnostics currently report `host_refused` with `hostEffect=unknown`, because CE may have created temporary compiler files before failing.
Require that exact bounded error shape for invalid compiler inputs, record the changed owned temporary files, and prove that no destination or protected partial output was published.
This expected failure case does not authorize continuing after a timeout, lost response, unexpected error shape, or unaccounted file effect.

## Phase 4: raw CE temporary output lifetime

This phase uses one reviewed qualification-only fixed bridge that calls `compileCS` with the valid payload and returns only the generated filename.
The bridge is test infrastructure, is not an MCP tool, accepts no caller arguments, and must match the reviewed source and payload hashes.
The bridge must not inject, load, invoke, delete, rename, or copy the generated assembly.

1. With A and B running, call the fixed raw bridge once in A and record the returned path in redacted form.
2. Require the returned raw path to be a new regular file under A's run-owned temp directory with no reparse point or protected-path overlap.
3. Stop immediately if the raw path is outside A's run-owned temp directory, and do not read, delete, or otherwise modify an out-of-scope path.
4. Record the raw file's length and SHA-256 through a read-only held handle.
5. Keep the exported `<run>/compiler/A/output/probe.dll` from phase 2 and record its length and SHA-256 again.
6. Stop private host B and its owned fixture normally while A and A's owned fixture remain under harness control.
7. Wait for B to exit and for a fixed two-second filesystem quiet interval.
8. Record whether the raw A artifact exists, and record its length and hash again only when it still exists.
9. Require the exported artifact to exist with its original length and hash after B exits.
10. Require A's instance and target to remain selected and responsive after B withdraws.
11. Stop private host A normally and wait for a fixed two-second filesystem quiet interval.
12. Record whether the raw artifact exists after A exits without treating either existence or expiry as a product failure.
13. Require the exported artifact to remain present with its original length and hash after A exits.
14. Classify raw lifetime as `expired_after_b`, `retained_after_b_expired_after_a`, or `retained_after_a` from the observations.
15. Product guidance must promise only the exported copy and must report the observed raw classification as CE-owned environment behavior.

This sequence distinguishes raw CE expiry from export retention because the raw file and exported file have separate paths, hashes, observations, and ownership.
No missing raw file may be reported as a missing exported file.
No retained exported file may be used to claim that CE's raw file survived.
Because A and B use distinct temporary directories, this sequence qualifies that isolated configuration only; it does not establish cleanup behavior when CE instances share a temporary directory.

## Optional phase 5: separate managed injection

This phase is excluded unless the maintainer separately approves managed injection in the same reviewed session.
Compiler qualification may pass without this phase, but the roadmap's managed-injection qualification remains open.
The phase is allowed only when the disposable target for the selected architecture loads a supported .NET runtime and exposes no user data or unrelated code.
The only permitted assembly is the exported phase-2 file whose length and SHA-256 still match.
The only permitted call is class `CheatEngineMcpCompileCsProbe`, method `Run`, parameter `CEMCP-COMPILECS-PROBE-V1`.
The expected signed decimal result is `7319`.

1. Start a fresh A host and a fresh owned disposable target for the selected architecture.
2. Verify the target process identifier, executable hash, architecture, loaded runtime, and ownership immediately before injection.
3. Call `exec_inject_dotnet` once with the exported phase-2 assembly and the fixed class, method, and parameter.
4. Require decimal result `7319` and require the target to remain responsive until its normal stop marker is written.
5. Record call duration, gateway deadline, CE and target exit states, and the tool's reported host effect.
6. Do not call another method, retry after timeout, inspect unrelated target state, or promise assembly unload.
7. Stop and preserve evidence on any timeout, lost response, wrong result, crash, target change, or unknown effect.
8. Repeat this phase in a separate run for x86 and x64 rather than switching architecture inside one session.

## Measurements and evidence

The report must use the evidence fields defined in [acceptance.md](acceptance.md) and remain redacted before commit.
It must record the policy revision, authorization decision, candidate commit, clean-tree state, package hashes, payload hashes, and configuration.
It must record the Windows build, CE fingerprint and version, .NET runtimes, target architecture, fixture hash, and compiler availability evidence.
For every call it must record the instance, operation, start and end time, duration, expected result, actual result, error kind, host effect, retryability, and correlation identifier.
For every relevant file it must record a run-relative redacted path, existence observation, length, SHA-256, and observation time.
It must record process exits, discovery withdrawal, source-installation identity, user-state restoration, cleanup failures, private-byte deltas, and handle-count deltas.
It must not record bearer tokens, discovery-record bodies, absolute personal paths, e-mail addresses, caller source, or unredacted logs.

## Stop conditions

Stop before launch when approval, either acknowledgement, a required gate, a candidate hash, a payload hash, or a fixture identity is missing or different.
Stop before the compiler call when the source installation, reviewed autorun set, private copy, backup guard, run roots, or process inventory fails validation.
Stop during the run on any kernel, DBVM, debugger, Auto Assembler, unsafe-Lua, external-network, shell, unexpected process-launch, or non-fixture target activity.
The reviewed CodeDOM compiler backend may start its installed C# compiler as part of compilation; this exception does not permit source-controlled process launches or execution of the generated assembly.
Stop on an out-of-root raw path, cross-instance effect, cross-target effect, unexpected file, unbounded diagnostic, crash, hang, timeout, lost response, unexpected or unaccounted mutation, or false success.
Stop on failure to withdraw a stopped instance, stop an owned process, restore user state, preserve the source installation, or account for an owned output.
After a stop, do not retry a compile or injection automatically.

## Cleanup and restoration

Stop fixtures through their owned stop markers and stop private CE hosts through the existing graceful path before any forced termination.
Close the gateway and require all owned processes to exit within the existing harness deadlines.
Verify discovery withdrawal and reject every stale instance identifier.
Restore the guarded registry and application-data state and verify the restoration before deleting a run-owned scratch file.
Verify the installed Cheat Engine fingerprint and runtime configuration are unchanged.
Delete exported outputs, invalid-reference fixtures, and raw artifacts only when their resolved paths remain below the current run directory.
Never delete a raw path that failed the run-root check.
Preserve the run directory and recovery marker when cleanup or restoration is unconfirmed.
Record every cleanup failure and do not report the run as passed when one remains.

## Pass criteria

Compiler and export qualification passes only when phases 1 through 4 complete on the exact candidate package for both x86 and x64 fixture runs.
The disabled gate must refuse before dispatch or files, valid source must export a verified bounded assembly, and invalid inputs must fail without published output.
The B-shutdown sequence must record the raw CE artifact outcome and must prove the exported artifact remains unchanged after both B and A close.
All owned processes, files, discovery records, and user-state changes must have confirmed cleanup or restoration.
The report must state the observed compiler prerequisites and raw-lifetime classification without generalizing beyond the exact environment.
Optional phase 5 qualifies only the fixed managed injection case on the architectures actually run.
No result from this policy qualifies arbitrary C# compilation, arbitrary managed code, general assembly loading, kernel execution, debugger behavior, or production targets.
