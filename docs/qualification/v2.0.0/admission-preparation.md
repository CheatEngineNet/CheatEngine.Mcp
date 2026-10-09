# Isolated admission measurement preparation

Status: implemented in an isolated snapshot, managed-tested and independently reviewed by Astra high with no remaining actionable findings in this scope. No new native session or performance qualification is claimed.

Snapshot: `artifacts/performance-preparation/admission-dcbf0ddf13a04534a0bc83af3fdeb37b`, copied from the previously reviewed [performance preparation](performance-preparation.md). The primary source, ordinary package and pending Settings-observation inputs remain unchanged. The source inventory verifies 861 files with exactly seven new or modified files relative to that prior snapshot.

## Prepared measurement

Each of the three hash-pinned beta.2 baseline sessions and three candidate sessions will open two independent gateway processes. Four rounds each issue eight 1,024-item int32 batches against owned host A, split equally between gateways, alongside discovery, a host-B sentinel read, one serialized CE UI snapshot, a one-item regions resource read, and module completion. All requests in a round settle before the next round or cleanup. Before/after checks compare retained jobs/resources on both hosts and verify the four optional gates are disabled.

Every sample records its client, category, monotonic start/end timestamps and outcome. Only exact protocol/tool `busy` with `hostEffect=not_started` counts as a refusal. Timeouts, cancellations, unexpected failures and empty completions remain distinct. Failure receipts preserve bounded diagnostics, inner/stack information when available, operation and error correlation. A failed peer does not let the measurement return while other requests remain active.

Comparison requires three complete baselines and one complete candidate: four rounds, thirteen uniquely identified requests per round, the exact client/category distribution and interval-derived concurrency. It requires cross-gateway batch overlap, and every observer or busy batch must overlap successful batch work in the same round. Missing successful/refused response classes, unloaded observations, or empty completions cannot become success evidence. Successful batch p95 uses the 1.5-times baseline budget; other measured response classes use the short-call allowance. The worst of three baseline p95 values is retained for each class.

These intervals describe client calls, not native dispatch occupancy. The UI observation includes the CE driver's 100 ms timer and file exchange. First/repeated module completion is labeled separately; repeat calls may hit the five-second cache. Empty completion does not establish a busy refusal, and its refresh may outlive the response. Four observations of an observer category are a fixed diagnostic sample set, not broad statistical qualification.

## Managed verification

Release build passed with zero warnings/errors. The exact four-class selection passed **52 tests, zero failures, zero skips**: ten measurement tests, twenty-two comparison tests, nine performance-matrix tests and eleven lifecycle-helper tests. Logs and TRX reside inside the snapshot; exact source/log/test-module hashes are recorded in `artifacts/performance-preparation/admission-evidence.json`.

| Requirement | Evidence |
| --- | --- |
| Wait for peers after failure and active cancellation | `MeasureAsync_WaitsForEveryPeerAfterOneFails`, `MeasureAsync_ActiveCallerCancellationSettlesAndClassifiesTheInvocation` |
| Refuse invalid request lists before starting work | `MeasureAsync_ValidatesEveryRequestBeforeInvokingAny`, `MeasureAsync_RejectsMoreThanSixteenRequestsBeforeInvokingAny`, `MeasureAsync_PreStartCancellationInvokesNothing` |
| Distinguish actual protocol refusals, malformed failures and empty completion | `ClassifyTool_RequiresExactBusyHostEffectAndRejectsMalformedErrors`, `Classifiers_DistinguishProtocolToolFailureAndEmptyCompletion` |
| Preserve failure diagnostics and correlation with bounded output | `Classifiers_RetainBoundedDiagnosticsAndProtocolCorrelation` |
| Reject missing refusals or a deficient baseline | `Compare_FastSuccessfulRequestsCannotReplaceMissingRefusals`, `Compare_OneBaselineMissingAnOutcomeCannotBeReplacedByTheOtherTwo` |
| Reject observer-only overlap and unloaded observations | `Compare_UiOverlapCannotReplaceCrossGatewayBatchOverlap`, `Compare_RejectsUnloadedObserverAndBusyTiming` |
| Refuse malformed rounds/clients/timing claims and enforce budget boundaries | `Validate_RejectsIncompleteOrMisreportedEvidence`, `Compare_AppliesResponseSpecificBudgetAtBoundary` |

Successful commands from this snapshot:

```powershell
dotnet build tests/CheatEngine.Mcp.Tests/CheatEngine.Mcp.Tests.csproj -c Release --no-restore -p:EnableSourceControlManagerQueries=false -p:RepositoryCommit=0f722ef7d6b37eee0985073dfe594c6559b31c14 -v:minimal
dotnet artifacts/bin/CheatEngine.Mcp.Tests/release/CheatEngine.Mcp.Tests.dll --filter-class CheatEngine.Mcp.Tests.LiveQualification.LiveAdmissionMeasurementTests --filter-class CheatEngine.Mcp.Tests.LiveQualification.LiveAdmissionComparisonTests --filter-class CheatEngine.Mcp.Tests.LiveQualification.LivePerformanceMatrixTests --filter-class CheatEngine.Mcp.Tests.LiveQualification.LiveLifecycleQualificationTests --fail-skips on --report-trx --results-directory artifacts/admission-final-results
```

The initial build found an invalid test JSON literal; a later build found formatting/analyzer violations. Both were corrected before the first passing 49-test run. Astra then found that observer timing could occur outside active batches and that exception classification discarded diagnostic details. The repairs added overlap controls and bounded diagnostics, followed by the clean 52-test run above. Astra's second read-only pass found no remaining actionable correctness findings in this admission preparation.

## Remaining native enumeration gap

The actual package source establishes that 4,096-module and 16,384-region collection sizes are destination bounds, not native traversal bounds. Client allocates that destination; SDK invokes CE's whole-table producer first, inspects the complete Lua sequence, and returns `DestinationTooSmall` before copying if necessary. CE's module iterator runs to exhaustion; its region iterator starts at zero and has no item cursor. Module-detail navigation also enumerates through `ModuleLocator.Find`.

Package provenance: Client `v1.0.0` tag `110a302b0908cbf8d71e4f5cdf1f0f0e6e7fe31cfc`; SDK `2.0.0` source `325c47b573f8bd39a247f1d0101f110fa36c1696` and lock-matching package content hash. References: [Client inspection](https://github.com/CheatEngineNet/CheatEngine.Client/blob/v1.0.0/libs/CheatEngine.Client.Core/Domains/InspectionClient.cs), [SDK inspection](https://github.com/CheatEngineNet/CheatEngine.SDK/blob/325c47b573f8bd39a247f1d0101f110fa36c1696/libs/CheatEngine.SDK.Engine/Inspection/EngineInspection.cs). Installed CE binary equivalence to the inspected public CE source remains unproven.

The separate [prepared-navigation repair](navigation-preparation.md) now preserves module/detail/region resources and module completion through five-second immutable snapshots prepared by explicit source tools, with process/epoch validation and truthful cold/stale errors. Its isolated implementation passes 4,207 portable tests. In that snapshot, admission qualification explicitly prepares modules/maps before timing each round and distinguishes the baseline completion cache from the candidate fresh policy. Final 9.01/9.03/9.05 qualification, integration, candidate packaging and native runs remain open.
