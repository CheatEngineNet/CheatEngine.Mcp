# Remaining work

Cleanup checkpoint: 2026-10-25. This is the only active checklist.

The user has asked to stop the repeated qualification loop and tidy the repository.
The records below remain open where evidence is missing; they are not instructions to
restart live probes. Resume a blocked item only when its listed prerequisite changes
and the user requests that work. Do not repeat an unchanged diagnostic attempt.

## Evidence already recorded

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
  DLL/PDB registration fixtures do not qualify source enabling.
- [ ] **Six kernel operations:** qualify `kernel_initialize_dbvm`, `kernel_poll_watch`,
  `kernel_read_physical`, `kernel_start_watch`, `kernel_translate_address` and
  `kernel_write_physical` in a separately reviewed suitable environment, including cleanup.
  This host has not met that prerequisite. Do not change drivers, VBS, Spectre mitigations,
  boot or virtualization settings as part of cleanup. See the [kernel plan](docs/qualification/all-tools/later-kernel-test.md).

## Gates for a future qualified release

- [ ] Review a Client host profile for the intended CE/SDK tuple and obtain fresh receipts.
  The recorded CE 7.7.1.10828 source-build tuple does not match the old 7.7.0.10621 profile.
- [ ] Freeze one final plugin/gateway/source candidate and run all required native success,
  restart and cleanup cases against those exact binaries. Never pool different candidates.
- [ ] Regenerate the per-tool report and reconcile documentation from passing receipts.
  Keep `nativeQualificationComplete=false` until every applicable native case passes.
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
