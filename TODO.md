# Remaining work

Maintenance checkpoint: 2026-10-27. This is the only active checklist.

The user requested local release repairs and bounded debugger validation after the cleanup.
The historical records below remain open where evidence is missing; they are not instructions
to restart diagnostics. Resume a blocked item only when its listed prerequisite changes
and the user requests that work. Do not repeat an unchanged diagnostic attempt.

## Accepted qualification limits for 2.0

On 2026-10-27 the maintainer explicitly deferred kernel/DBVM native qualification because
a suitable environment is unavailable. These six operations remain unqualified and are
excluded from the release-blocking native cases for 2.0:
`kernel_initialize_dbvm`, `kernel_poll_watch`, `kernel_read_physical`, `kernel_start_watch`,
`kernel_translate_address` and `kernel_write_physical`.

Document this limitation in the release; do not count the deferral as a passing native test.
Keep the existing capability gates. Do not change drivers, VBS, Spectre mitigations, boot or
virtualization settings to close this item. The [kernel plan](docs/qualification/all-tools/later-kernel-test.md)
is retained for future work only; resume it only on a new maintainer request with suitable prerequisites.
This deferral does not include `symbol_enable_sources` or the other release gates below.

## Evidence already recorded

- Double tool-test audit: **207 of 207 tools** have executed automated evidence. Every
  backend tool has a reviewed positive adapter mapping, and every gateway tool has routing
  or discovery tests. Independent assertion review covered all 207 tools and led to new
  option/state/discovery cases, corrected mappings and a duplicate-evidence guard.
  Full Debug/Release portable suites each passed **5,966**, and NativeLua passed **822**;
  builds, formatting, isolated plugin load and Native AOT smoke checks passed.
  See the [audit and per-tool matrix](docs/qualification/all-tools/test-audit.md).
  This is automated evidence; native qualification remains separate under the accepted
  kernel/DBVM deferral and the remaining gates below.
- Maintainability and servicing candidate: Debug and Release portable suites each passed
  **5,959 tests**, NativeLua passed **816**, and the SDK repository policy suite passed **202**.
  Two later runtime-notice provenance cases passed in each configuration; the focused notice
  runs also repeated five existing attribution cases. Locked restores, zero-warning builds,
  formatting, isolated Debug/Release plugin probes, Native AOT gateway smoke checks and local
  ZIP/checksum packaging passed. The SDK is pinned to **10.0.403**, with bundled runtime and
  AOT packs **10.0.14**, including their upstream component notices.
  Shared gateway policy, narrow plugin status operations, test fixtures and split Record/live
  harness files retain their existing contracts; source-equivalence checks cover 32 Record
  declarations and 85 live-harness members. See the
  [candidate verification](artifacts/maint-release-20261027/verification.json).
  This candidate remains `2.0.0-beta.4`, with no native-host sessions run against its binaries.
  Earlier native receipts below do not qualify it; its distribution manifests correctly report
  `releaseEligibility.eligible=false` while source/dependencies remain uncommitted.
- Local release repairs: Debug and Release portable suites each passed **5,934 tests**, and NativeLua passed
  **789 tests**. Isolated Debug/Release distribution probes, Native AOT gateway smoke checks and local packaging passed.
- Kernel/source follow-up: the Release portable suite passed **5,940 tests**. The final real-Lua suite passed
  **809**, and the final focused Release/Debug checks passed **82/891** after adding the last test-only edge cases.
  Watch removal now requires CE's inactive-ID confirmation, physical writes require an explicit acknowledgement,
  and source API availability is checked before effects. This is adapter evidence, not native kernel qualification.
  See the [follow-up verification](artifacts/kernel-source-20261027/verification.json).
- The repaired candidate passed separate `debugger-basic`, `debugger-events` and `debugger-execution` Windows-debugger
  sessions on 2026-10-27, with matching product hashes, restored user state, unchanged installed CE files and no cleanup
  failures. The first basic run exposed and corrected a stale fixture return-address oracle; its failed receipt is retained.
  See the local [verification record](artifacts/release-fixes-20261027/verification.json). This does not qualify the whole
  native catalog or establish the cause of the historical debugger failure.
- Exact bundle 06 / current-27: **200 of 207 advertised tools verified**, seven unverified.
  This evidence belongs to that bundle, not to later source builds.
- Active plugin disable/re-enable, capture cleanup, allocation release and old-port closure
  passed in the separately pinned `20261022T215930Z-2235` session.
- The `C000050B` run-root refusal has a bounded, high-confidence BindFlt redirect diagnosis.
  Keep the no-reparse guard; the trace limitations remain part of the finding.
- The source-built plugin was loaded and a bounded memory-read smoke check passed on
  2026-10-25. This does not qualify its Client tuple or the complete native tool catalog.

Exact hashes, failures, receipts and limitations remain in local ignored artifacts and
Git history. The removed run journals are backed up under
`artifacts/cleanup-20261025/document-history/`; they are not additional tasks.

## Blocked native work

- [ ] **Historical provider-state failure:** establish the cause of the old `-101889`
  state. Resume only with new evidence containing the provider pointer, thread IDs,
  Lua state identity/generation and raw fields, alongside healthy-state calibration.
  Successful replays establish non-reproduction only.
- [ ] **Historical debugger/SDK failures:** establish the monitor-overlap `SEHException`
  cause and account for the earlier frame-restoration/compiler failure. Resume only with
  a matching native dump/stack, event sequence and exact module identities, or another
  concrete new artifact. Do not rerun the unchanged observer scenario.
- [ ] **`symbol_enable_sources`:** obtain a reviewed network-isolated execution path or
  a separately qualified local-only CE API, a matching image/PDB and independent effect
  readback. The audited current CE path selects a public symbol server; existing local
  DLL/PDB registration fixtures do not qualify source enabling. Requested-API preflight and
  kernel-only failure reporting were repaired locally; these do not establish native success.
  See the [source qualification plan](docs/qualification/all-tools/symbol-source-test.md).
## Gates for a future qualified release

- [ ] Review and commit the local MCP and SDK servicing changes in their own repositories,
  then record the reviewed dependency revisions in MCP. The SDK pin, documentation and implicit
  tool locks changed locally; its production implementation and ABI did not change in this pass.
  Freeze only after those source/dependency trees are clean and reproducible.
- [ ] Obtain fresh MCP receipts for the exact CE/SDK tuple. Client's catalog profile still
  describes CE 7.7.0.10621; it reports qualification status rather than admitting operations.
  Keep Client's `Unknown/NotExecuted` status honest. MCP receipts for CE 7.7.1.10828 do not
  qualify or rename that Client profile.
- [ ] Freeze one final plugin/gateway/source candidate and run all required native success,
  restart and cleanup cases against those exact binaries, excluding the six deferred kernel
  operations above. Never pool different candidates.
- [ ] Regenerate the per-tool report and reconcile documentation from passing receipts.
  Keep the whole-catalog `nativeQualificationComplete=false` while any native case remains
  unqualified, including deferred cases. Record the accepted 2.0 limitation separately from
  the results of its required native cases.
- [ ] Complete the previously requested fresh independent final qualification review
  (Astra), with required Release/Debug, formatting, packaging and fuzzing checks.
  A bounded cleanup review does not satisfy that final release gate.
- [ ] Publish only when explicitly requested, then verify the remote commit and artifacts.
  Local cleanup commits do not authorize a push or another beta release.

## Repository handoff

Client and SDK changes belong in their own Git repositories, followed by the MCP commit
that records their revisions. Local integration commits may precede upstream review;
they must be published/reviewed upstream before a distributable release relies on them.
Preserve ignored qualification artifacts, rollback binaries and installed CE/user state.

See [all-tool qualification](docs/qualification/all-tools/README.md) for the evidence map
and [CONTRIBUTING.md](CONTRIBUTING.md) for normal build/test commands.
