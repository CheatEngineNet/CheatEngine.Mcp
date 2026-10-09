# Isolated prepared navigation repair

Status: implemented, portable-tested and independently source-reviewed by Astra high with no actionable findings in this scope. Final Release build passed with zero warnings/errors; all 4,207 portable tests passed with zero failures/skips. No native execution or integration into the primary source is claimed.

Snapshot: `artifacts/performance-preparation/navigation-9c315236323c4d41bf2d60075d20e411`, copied from the verified 861-file [admission preparation](admission-preparation.md). Its final inventory has 863 files: 26 modified and two new relative to that snapshot. The ordinary package and pending Settings-observation inputs remain unchanged; both no-execution preflights still verify 859 inputs.

The later [navigation follow-up](navigation-followup.md) repairs the remaining thread/symbol/breakpoint routes and corrects the explicit preparation tools' dispatch classes. This earlier snapshot and its separate local package retain the older labels; their review and test results do not supersede those subsequent findings.

## Behavior

Explicit attached-process `module_list`, `module_get`, and `memory_list_regions` calls prepare immutable inspection data for five seconds. Navigation reads perform one admitted current-target check, then use that prepared data. Module, region and section enumeration, symbol lookup and PE/PDB reads are absent from these prepared navigation paths. The explicit tools retain their native enumeration behavior; destination limits still do not establish native traversal bounds.

Module and memory-map publications share a process ID, selection epoch and revision. A newer epoch invalidates old data; delayed older publication is refused. Module-only publication invalidates a prior map. Every read checks freshness against a monotonic clock; the exact five-second boundary is expired. Cold, stale or wrong-target resources return `invalid_state` with an explicit preparation hint. Completion offers no values on those failures.

Module detail retains only the latest successful `module_get` result, accepting its canonical name or exact normalized selector. Each returned sections array is detached from the retained copy. Any later module or memory-map publication invalidates that detail result. Calls to `module_list` with an explicit `processId` do not populate or replace the attached-target snapshot.

The new module-completion policy joins one in-flight listing and keeps the existing global dispatch admission and 750 ms wait. Successful results have no outer completion cache that could extend snapshot lifetime or hide a changed target; failed listings retain a one-second retry interval. Other completion policies remain unchanged. Request intervals and wait deadlines do not prove native dispatch occupancy.

Resource URIs, tool names and JSON schemas are preserved. Requiring explicit preparation before navigation is an intentional behavior change, described in tool/resource metadata and embedded documentation. Admission qualification now prepares modules and regions before each timed round, recording those calls separately; that native workload remains unexecuted.

## Verification

| Requirement | Evidence |
| --- | --- |
| Exact expiry and process identity | `Modules_ExpireAtTheLifetimeBoundaryAndRejectWrongIdentity`, `IsCurrent_RejectsExpiredVersion` |
| Reject delayed old publication | `Modules_OlderEpochCannotReplaceNewerPublication`, `MemoryMap_StalePublicationAfterNewerModulePublicationIsRejected`, `ReadOfNewerTargetRejectsDelayedOlderPublication` |
| Atomic map/module version and invalidation | `MemoryMap_PublicationSharesModuleVersionAndModuleRefreshInvalidatesMap` |
| Preserve prepared projections without further inspection | `PreparedReads_EqualTheirExplicitPreparationAndDoNoFurtherInspection`, `ListPreparedRegions_ProjectsTheExactDefaultExplicitResultWithoutInspectionCalls` |
| Retain complete raw data after filtered source calls | `ListPreparedRegions_UsesTheCompleteMapAfterAnExplicitFilteredRead` |
| Prevent other-process and caller-array contamination | `List_OtherProcess_DoesNotPoisonTheAttachedPreparedTarget`, `PreparedDetails_AreDetachedFromExplicitAndPreparedCallers` |
| Fresh completion, coalescing and failure throttle | `Complete_FreshDispatchSuccessesAreNeverServedFromTheOuterCache`, `Complete_FreshDispatchConcurrentRequestsShareOnlyTheirInFlightListing`, `Complete_FreshDispatchFailureIsThrottledUntilItsRefreshInterval` |
| Cold, expired and switched-target wire behavior | `CompleteOverTheWire_ColdModulesNeverEnumerate`, `CompleteOverTheWire_ExpiredOrChangedTargetNeverOffersStaleModules` |
| Stable protocol surface | Reviewed backend/gateway tool and resource goldens; non-capture `ContractSnapshotTests` and `ToolCatalogContractTests` |

Final verification from the isolated snapshot:

```powershell
dotnet build tests/CheatEngine.Mcp.Tests/CheatEngine.Mcp.Tests.csproj -c Release --no-restore -p:EnableSourceControlManagerQueries=false -p:RepositoryCommit=0f722ef7d6b37eee0985073dfe594c6559b31c14 -p:SourceRevisionId=0f722ef7d6b37eee0985073dfe594c6559b31c14 -v:minimal
dotnet artifacts/bin/CheatEngine.Mcp.Tests/release/CheatEngine.Mcp.Tests.dll --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on --report-trx --results-directory artifacts/navigation-portable-final-results
```

The final portable run passed **4,207/4,207**, zero skips, in 1 minute 44.590 seconds. Its scope includes the focused navigation and admission classes, contract snapshots, packaging fixtures, knowledge lint and remaining managed regressions. NativeLua and live qualification were explicitly excluded. Build, test module, log, TRX and every source hash are recorded in `artifacts/performance-preparation/navigation-evidence.json`; the recorder also verifies the prior snapshot and unchanged Settings manifests. The informational version identifies the base commit plus this separately hashed uncommitted source inventory; it does not identify a clean release candidate.

Before the final run, 214/214 focused cases and 38/38 non-capture contract cases passed. The first full portable run passed 4,196/4,207: one documentation lint failure omitted the tool name for `maxOffset`, and ten packaging fixtures failed because disabling SCM queries removed source-revision metadata. The documentation now names the tool, stays within its existing byte budget, and the final build explicitly supplies `SourceRevisionId`. No packager behavior was changed. Reviewed golden changes contain only preparation descriptions and embedded document sizes; names, URIs and schemas remain unchanged.

Astra's read-only source review and final evidence review found no remaining actionable findings in this repair. The final review independently verified all 863 snapshot hashes, 861 prior hashes, both 859-input Settings preparations, test counters, build identity, documentation budget and the four constrained golden changes. Integration, candidate packaging, native latency/admission tests and the wider lifecycle/release matrix remain open; this repair alone cannot close #28, #31 or #33.
