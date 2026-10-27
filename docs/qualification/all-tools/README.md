# Full tool qualification

The catalog contains 206 backend tools plus gateway `instance_list`. A verified native
case needs exact candidate identity, successful operation receipts, an independent result
or effect oracle, and cleanup evidence where applicable. Routing, refusals, portable tests
and Lua adapter tests do not establish native tool success.

## Current status

The latest catalog-wide native report remains **current-27 (2026-10-21): 200 verified,
7 unverified**, with `nativeQualificationComplete=false`. It applies only to bundle 06:

| Identity | Value |
|---|---|
| Product | `2.0.0-beta.4+1f987742b7fcd7760057a19508ddacbf2aad4bd8` |
| Plugin SHA-256 | `E05A449551716AC485E2C5897397D109584B94CBEC3444FD8C7461432CF0462B` |
| Gateway SHA-256 | `1FEEFE4975AE558C78932B63C93BAE7A97EB6DBA571C76402B87B63B0406467A` |
| Report SHA-256 | `E5A8293487BCCF155C28DBB72311FC86832A9F00FD54901F22B0123AD50C2BCE` |

The local [report](../../../artifacts/all-tools-goal/all-tools-current-27/tool-results.json)
contains the per-tool classifications. The seven open tools are `symbol_enable_sources`
and the six DBK/DBVM-dependent operations listed in [the current checklist](../../../TODO.md).
Later source changes and the separately qualified active-lifecycle scenario do not inherit
bundle 06's catalog-wide result.

## Accepted 2.0 qualification limit

On 2026-10-27 the maintainer deferred native qualification of the six DBK/DBVM operations
in the [kernel plan](later-kernel-test.md). They remain unqualified and are excluded from
the release-blocking native cases for 2.0 because a suitable environment is unavailable.
The deferral leaves their capability gates and historical results intact. Keep the
whole-catalog `nativeQualificationComplete=false`; an accepted release limitation is
separate from evidence that a native operation passed. `symbol_enable_sources` remains
open independently, along with the other [release gates](../../../TODO.md).

## Where to find the evidence

| Record | Purpose |
|---|---|
| [Current checklist](../../../TODO.md) | One list of remaining requirements and the prerequisites for resuming them |
| [Automated double audit](test-audit.md) | All 207 tools mapped to executed tests, assertion review and repaired evidence gaps |
| [Runner contracts](runners.md) | Maintained scenario boundaries and evidence requirements |
| [Kernel qualification plan](later-kernel-test.md) | Suitability, independent oracles and recovery prerequisites |
| [Symbol-source qualification plan](symbol-source-test.md) | Network isolation and independent symbol readback prerequisites |
| [Test project guide](../../../tests/CheatEngine.Mcp.Tests/README.md) | Portable, NativeLua and explicit live test execution |

Old checkpoint documents and release drafts were removed from the active tree. Their local
backups are under `artifacts/cleanup-20261025/document-history/`, and prior committed versions
remain in Git history. Raw evidence stays under ignored `artifacts/`; none of it is a second
work queue. Do not repeat unchanged diagnostic runs or mark an environment blocker passed.

## Reproduce the evidence report

After a locked restore and Release build, create a portable TRX from the repository root:

```powershell
dotnet test --project tests/CheatEngine.Mcp.Tests -c Release --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on --report-trx --report-trx-filename portable.trx --results-directory artifacts/all-tools/portable
```

The output is `artifacts/all-tools/portable/portable.trx`. This suite includes gateway-routing
tests, so supply that same file to **both** `-GatewayTrx` and `-PortableTrx` when calling
[`eng/Write-AllToolQualificationReport.ps1`](../../../eng/Write-AllToolQualificationReport.ps1):

```powershell
$portableTrx = './artifacts/all-tools/portable/portable.trx'
./eng/Write-AllToolQualificationReport.ps1 -GatewayTrx $portableTrx -PortableTrx $portableTrx -OutputDirectory './artifacts/all-tools/portable-report'
```

Use [the test guide](../../../tests/CheatEngine.Mcp.Tests/README.md) to create a separate
NativeLua TRX and pass its actual path with `-NativeLuaTrx` when that suite has been run.
Use a new output directory for each evidence report; the portable-only example does not
establish NativeLua or native-host qualification.
For native qualification, pass the exact candidate's session summaries with `-LiveSummaries`
as a PowerShell array. Do not combine different plugin/gateway hashes or change a frozen
candidate during a live session.

The output is `tool-results.json`. A failed session is `observed_in_failed_session`, never
`verified`; missing or duplicate required receipts leave the case unqualified. Review each
row and the session restoration/cleanup result before changing the checklist.
