# Automated tool audit

The 2026-10-27 double audit covers **206 backend tools plus gateway `instance_list`**.
All 207 have executed automated evidence. Every backend tool has a reviewed positive
adapter case, and every advertised tool has passing gateway routing or discovery tests.
This measures tool-to-test evidence, not line or branch coverage or native CE qualification.

## Two audit passes

1. Compare the production catalog with the reviewed evidence map, existing test methods,
   source paths and exact executed TRX cases. Catalog-derived tests enforce the same inventory
   at runtime, so a tool addition cannot silently omit its adapter mapping or routing cases.
2. Independently review each tool's public path and actual assertions in two disjoint groups
   of 103 and 104 tools. Review the fixes again after addressing meaningful gaps. This was
   static source-to-assertion reasoning; no mutation runner was used.

The audits found and repaired weak option coverage for SHA-1 hashing, Page Exception
breakpoints and Write captures. Discovery now has direct tests for stale candidates,
partial results on its manual-clock deadline and caller cancellation. Speedhack state has
normal/non-default speed and absent/present symbol cases, with unchanged effects asserted.
The discovery test seam preserves the public constructor's system clock and ten-second budget.

The evidence map also incorrectly attributed `record_find` and `speedhack_get_state` to
tests that did not invoke those operations, and duplicated `table_save`. The entries now
point to direct behavioral assertions, and the contract suite rejects duplicate case identities.
The duplicate guard was first observed failing on `table_save`, then passed after correction.

## Executed checks

| Check | Result |
|---|---|
| Full portable Release | 5,966 passed; zero failed or skipped |
| Full portable Debug | 5,966 passed; zero failed or skipped |
| Real Lua 5.3 adapters | 822 passed; zero failed or skipped |
| Positive backend adapter mappings | 206 of 206 tools, with 209 reviewed adapter entries |
| Gateway routing/discovery | 207 of 207 tools |
| Build and formatting | Debug/Release builds and style/whitespace verification passed |
| Distribution | Two isolated plugin load contexts and Native AOT gateway stdio smoke passed |

Use the [test guide](../../../tests/CheatEngine.Mcp.Tests/README.md) for commands and the
[contributor guide](../../../CONTRIBUTING.md) for SDK, build and formatting requirements.

## Records and limits

- [207-row tool matrix](../../../artifacts/tool-double-audit-20261027/tool-matrix.md)
- [Combined audit and exact test outcomes](../../../artifacts/tool-double-audit-20261027/double-audit.json)
- [Release qualification report](../../../artifacts/tool-double-audit-20261027/qualification-release-final/tool-results.json)
- [Debug qualification report](../../../artifacts/tool-double-audit-20261027/qualification-debug-final/tool-results.json)
- [Independent first-half review](../../../artifacts/tool-double-audit-20261027/behavior-a.md)
  and [second-half review](../../../artifacts/tool-double-audit-20261027/behavior-b.md)
- [Review of fixes](../../../artifacts/tool-double-audit-20261027/followup-review.md)
  and [evidence-map closeout](../../../artifacts/tool-double-audit-20261027/evidence-closeout-review.md)

These local records are ignored artifacts, with input/report digests recorded for review.
The separate Roslyn source-file pairing analyzer included generated candidate trees and
returned a partial report with parse errors. Its output is retained but is not used as
clean whole-tree source pairing or as a coverage percentage.

No native-host session ran against this candidate. Six kernel/DBVM operations remain
an [accepted 2.0 qualification limit](README.md#accepted-20-qualification-limit), and native
`symbol_enable_sources` remains open. Historical native receipts do not transfer to these
binaries. The whole-catalog `nativeQualificationComplete` flag remains false.
