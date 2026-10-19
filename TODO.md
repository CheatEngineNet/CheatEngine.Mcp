# Continuation checklist

Work paused at the user's request on 2026-10-19. This is a saved development checkpoint, **not completed
native qualification or a release**. Resume only when the user asks. Keep the existing goal paused.

## Checkpoint validation

- [x] Release solution build: zero warnings and errors (`artifacts/pause-checkpoint-build-2.log`).
- [x] Standalone Lua suite: 743 passed, zero failures/skips (`artifacts/pause-checkpoint-lua-2.log`).
- [x] Changed C# whitespace verification passed (`artifacts/pause-checkpoint-format-1.log`).
- [x] Portable Release suite: 5,728 passed, zero failures/skips (`artifacts/pause-checkpoint-portable-2.log`).
- [ ] Run the new checkpoint through Debug/CI and inspect the result after publication.

The initial checkpoint tests exposed a Mono numeric-type mismatch and an outdated debugger receipt
assertion. Those were corrected before the final checkpoint validation. No new CE session was started
after the pause request.

## First steps next time

1. Read this file, `docs/mcp-alternatives-research.md`, and `docs/qualification/all-tools/README.md`.
   Check actual Git state and CI before making changes. Preserve checked and unchecked evidence boundaries.
2. Rebuild after any source edit. Do not rebuild a running test apphost or change tracked sources/binaries
   during a live native session. Recreate the prepared input hash manifest after every source/binary change.
3. Verify all owned CE/Mono/fixture processes from the previous run have exited and user settings restoration
   succeeded. Use private CE copies and owned targets; preserve the installed CE directory and user state.
4. Continue the independent scenarios below. Check off an item only after its assertions **and cleanup** pass.

## Prepared work awaiting native verification

- [ ] Repeat x64 `workflows` on package 14 with the bounded manifest reader and isolated process-create case.
  The earlier run completed file-target recovery, managed object work and signature generation, but its
  later process-create failure invalidated the whole session. Finish target/gateway/host restarts and note persistence.
- [ ] Run the same updated `workflows` scenario on x86. Keep pointer-width and physical module-path checks.
- [ ] Run the isolated `process-create` scenario. The unchanged Framework executable worked from a short path
  and exited before `Main` at a 253-character executable path. The fixture now copies to `target.exe`, rejects
  paths >=240 before launch, and records the raw create response before waiting for its manifest.
- [ ] Run the private Mono scenario and qualify all 15 tools, including the fixed static and instance calls,
  exact object fields, complete instance-search page, collector detach, job absence and owned target exit.
  Only target-code execution is enabled; the other three gates remain disabled. Each method is invoked once;
  never retry an uncertain invocation.
- [ ] Run the revised debugger transition diagnostic. It captures unconditional callback count/raw primitive
  values plus independent owned entry-byte/hit/progress snapshots. Request context only after the exact owned
  thread/RIP event. Diagnostic sessions must never count as verified tool success.
- [ ] Use that evidence to finish debugger capture/trace/thread-ignore success cases and clean up every job,
  breakpoint, callback and debugger attachment. Do not infer breakpoint installation from list presence alone.
- [ ] Resolve same-process plugin reload. The reviewed Settings bridge still observes zero plugin rows after
  `loadPlugin` succeeds. No supported Lua-only discriminator or fix has been established; do not bypass
  ownership guards or guess unmanaged callback addresses.
- [ ] Resolve the intermittent SDK frame-restoration/compiler failure with new evidence; non-reproduction is
  diagnostic only. Re-run the compiler, managed-injection, C/ABI and patch cases on the final exact bundle.
- [ ] Finish remaining symbol source/reload cases without mistaking automatic discovery for an explicit effect.
- [ ] Qualify DBK/DBVM operations only in a suitable reviewed environment. Refusals and portable doubles do not
  establish successful kernel operation.

## Mono prerequisite and local assets

- [x] Verified the official Mono 6.12.0.206 x64 MSI with a valid Microsoft signature; extracted privately,
  without a global installation.
- [x] Inspected shipped `mscorlib.dll` IL and `object.h`: legacy 32-bit GC handle bits map to the native
  `mono_gchandle_get_target(uint32_t)` C ABI and return the object header pointer.
- [x] Built and ran the standalone owned fixture: matching PID/hash/nonce, nonzero pinned object pointer,
  expected primitive values, stop marker and exit code zero. This qualifies no MCP Mono tool.

Local ignored runtime: `artifacts/fixtures/mono-6.12.0.206-x64/extracted/runtime`.
MSI provenance and extraction inventories are in the parent folder. MSI SHA-256:
`4125F57D97CFA88257915EDC969E913DE198CD8E22396A29849037479A0AC368`.
Build the fixed helper with `eng/Build-LiveMonoTarget.ps1`; it refuses existing output files, checks reparse
ancestors, pins runtime inputs, and writes the identity sidecar under `artifacts/live-mono-target`.
The legacy compiler's output is not deterministic: pin actual EXE bytes in the prepared run manifest;
do not replace that with a transient hardcoded EXE hash in tracked test source.

Local runner helpers remain ignored in `artifacts/issue-edits`:
`Save-RoadmapInputs.ps1` (use `-IncludeMono` for the complete private runtime input set) and
`Invoke-RoadmapSourceWorkflows.ps1` (includes `process-create`, `mono` and the debugger diagnostic switch).
Raw live artifacts remain in the workstation's `CheatEngine.Mcp.LiveQualification/runs` folder.

## Evidence and completion

- [ ] Generate cumulative all-tool evidence with `eng/Write-AllToolQualificationReport.ps1 -LiveSummaries`.
  Pass a PowerShell array. Sessions must have identical product version/plugin/gateway hashes; operation
  and cleanup receipts must each appear exactly once in the same passing session. Keep failed/diagnostic
  observations separate. Every one of the 207 catalog entries needs its own honest evidence classification.
- [ ] Re-run every required success case on the final exact replacement bundle, with restart and cleanup proof.
- [ ] Update the roadmap and all-tools checklist from those passing receipts. Leave environment blockers unchecked.
- [ ] Run a **fresh independent Astra subagent** for final verification; fix findings and repeat until no
  actionable finding remains. Earlier bounded reviews do not satisfy this final requirement.
- [ ] Complete required Release/Debug/formatting/fuzzing checks, publish the verified final main state and verify
  its remote commit. Do not create another beta release until the user requests it.

Pinned dependency mains at this checkpoint: Client `5c0d3057c040920665579ca3b0f464f3cd865317` (merged PR 69),
SDK `7c5b681a4d8055129d7d51b28931e2f6c6d87361` (merged PR 99). Preserve Git submodules rather than reverting to NuGet.
Package 14's frozen product version is `2.0.0-beta.4+cfb2421426f32edcc8986caa1be1896872f7a4e7`;
its successful symbol session was `20261019T184727Z-76aa`. See the all-tools README for exact hashes and failures.
