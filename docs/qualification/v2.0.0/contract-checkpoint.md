# Stable v2.0.0 contract checkpoint

Date: 2026-10-08.
Status: Current-source audit with bounded clean-candidate evidence; final stable contract freeze remains incomplete.

## Candidate identity

The clean tested candidate is `808a626e65eddab113737a27a7c2345555b1c241` and still identifies as `2.0.0-beta.2+808a626e65eddab113737a27a7c2345555b1c241`.
Its Release plugin SHA-256 is `C07B5DBE73B8AE509C091850CE0F74501028CB868D4AEE0000F07E821B5419FC`.
Its Release Native AOT gateway SHA-256 is `7DB3AF0853D85198FE07280BCBB5A4047115D08A344FC31FB8FF6ABE5F23139D`.
Its local Release ZIP SHA-256 is `EB803348B3F953D89D735637CCE95F254DCA5EF66B2480CDCF72239453EBFB42`.
The clean candidate passed 4,037 portable tests in both Debug and Release, 611 NativeLua tests, both packaged-DLL golden comparisons, and the Native AOT executable checks recorded in `compiler-checkpoint.md`.
The historical pointer smoke used Release plugin SHA-256 `E6E2AE56B43CF54F1F5978B566EE762939B76E549D557FDDF8B073BB8262750B` and must not be attributed to the clean compiler candidate.
Neither candidate is the final stable package.

## Section 3 crosswalk

| ID | Current result | Exact existing evidence | Required candidate level and remaining work |
| --- | --- | --- | --- |
| 3.01 | partial | `ContractSnapshotTests.GatewayCatalog_CurrentBuild_MatchesGoldenSnapshot`, `ContractSnapshotTests.Backend_ToolsAndInitialize_MatchGoldenSnapshots`, `CompositionParityTests.SchemaOnlyCatalog_MatchesTheLiveBackendListing`, `CompositionParityTests.GatewayTools_AreTheBackendToolsWithOnlyTheRoutingArgumentAdded`, and the clean packaged-DLL comparison passed. | Repeat V+ and packaged reflection after final names, workflows, prompts, resources, and goldens freeze on the exact RC and final stable commit. |
| 3.02 | pass for the clean tested candidate | `CompositionParityTests.PluginAndGatewayExecutable_RegisterTheSameJsonResolvers`, both Debug and Release Native AOT checks, and the packaged-DLL golden comparison cover the three P1 input and result types. | Repeat V+, AOT, and packaged reflection on the exact RC and final stable commit. |
| 3.03 | partial | `OutputSchemaContractTests.ToolOutputSchemas_NeverRequireANullableMember`, `ContractJsonTests.Serialize_MinimalError_OmitsNullFields`, and domain portable tests cover many nullable results. | Audit every unavailable, zero, empty string, and empty collection result shape before contract freeze, then retain the audit with V+. |
| 3.04 | partial | Pointer portable and NativeLua tests cover signed offsets and width arithmetic, while schema goldens preserve string-form hexadecimal addresses. | Add an evidence audit through a real MCP client for maximum 64-bit addresses, negative offsets, exact integers above JavaScript safe range, and hexadecimal round trips. |
| 3.05 | pass for the clean tested candidate | `GatewayToolCatalogTests.RoutedTools_InstanceId_IsTheFirstPropertyAndRequirement`, `GatewayServerTests.CallTool_TwoPublishedInstances_RoutesOnlyToRequestedBackendAndStripsInstanceId`, `GatewayServerTests.CallTool_MissingInstanceId_ReturnsInvalidArgumentEnvelope`, `GatewayServerTests.CallTool_UnknownInstanceId_ReturnsInstanceUnavailableNotStarted`, and `GatewayServerTests.CallTool_StaleIdentity_ReturnsInstanceUnavailableNotStartedAndNeverForwards` cover explicit routing and disappearance. | Repeat portable V+ and the maintained two-instance VL on the exact final package. |
| 3.06 | partial | `McpContractRulesTests.BackendCatalog_ContainsOnlyValidatedV2Tools`, `McpContractRulesTests.ValidateTool_ReadOnlyAndDestructive_Fails`, `McpContractRulesTests.ValidateTool_PollNotReadOnlyOrNotIdempotent_Fails`, `ToolCatalogContractTests.RegisteredV2Tools_OpenWorldAnnotation_MatchesTheReviewedList`, and the generated `tool-summary.txt` cover structural rules. | Review every tool annotation, dispatch class, and capability against its implementation effect before contract freeze. |
| 3.07 | partial | `FeatureOptionsTests.ClientOptIns_FollowTheSameFeatureInstance`, `FeatureOptionsTests.ReadFeatureOptions_AbsentSection_EnablesEverySwitch`, compiler disabled-gate evidence, and domain gate tests cover direct paths. | Complete the loaded-table, record-script, indirect Lua, indirect Auto Assembler, and argument-dependent effect audit and retain effect-before-refusal evidence. |
| 3.08 | pass at documentation level | `McpConfigurationTests.Load_Defaults_AreLoopbackAndAllExecutionIsEnabled`, `McpConfigurationTests.Load_PluginUserAndEnvironment_UseDocumentedPrecedence`, README configuration text, `support-matrix.md`, and `acceptance.md` agree on enabled switches, empty roots, precedence, and exposure semantics. | Reconcile these statements against the exact RC configuration and final release notes. |
| 3.09 | partial | `ContractJsonTests.Serialize_FullError_KeepsTheEnvelopeFieldOrder`, `ContractJsonTests.TryRead_CreatedResult_RoundTripsTheError`, `GatewayServerTests.CallTool_BackendErrorResult_PassesThroughUnchanged`, `GatewayServerTests.CallTool_BackendProtocolError_PassesThroughWithItsCode`, and `ErrorCorrelationTests.CallTool_ReportedInternalError_KeepsItsDetailsAndLogsTheSameErrorId` cover typed envelopes and correlation. | Audit retryability, hints, details, and correlation for representative direct backend and routed failures at every host-effect class. |
| 3.10 | pass for portable routing behavior | `GatewayServerTests.CallTool_CancelledForward_DoesNotRetryAnotherInstance`, `GatewayServerTests.CallTool_BackendHang_ReturnsTimeoutUnknownAndNeverRetries`, and `GatewayServerTests.CallTool_BackendDropsConnection_ReportsUnknownEffectAndNeverRetries` prove the gateway does not replay uncertain calls. | Retain these tests in V+ and verify representative native mutation state after a timeout or lost response. |
| 3.11 | pass for the clean tested candidate | `ContractSnapshotTests.GatewayExecutable_StdioWithoutInstances_MatchesGoldenSnapshots`, `GatewayServerTests.ListTools_WithoutPublishedInstances_ExposesStableRoutedCatalog`, `GatewayResourceTests.ListResources_WithoutInstances_ServesTheDocumentsAndTheInstanceList`, `GatewayResourceTests.ReadResourceAndGetPrompt_Local_AreServedWithoutAnyBackend`, and `GatewayCompletionTests.Complete_AllowedValuesOfARoutedTemplate_AreServedByTheGatewayWithoutAnyBackend` cover no-instance behavior. | Repeat AOT executable and V+ checks on the exact RC and final stable gateway. |

## Source boundaries reviewed

The contract tests are in `tests/CheatEngine.Mcp.Tests/Contract/ContractSnapshotTests.cs`, `CompositionParityTests.cs`, `OutputSchemaContractTests.cs`, `ToolCatalogContractTests.cs`, and `ToolContractTests.cs`.
The serialization and catalog rules are tested in `tests/CheatEngine.Mcp.Tests/Core/ContractJsonTests.cs` and `McpContractRulesTests.cs`.
The gateway routing, resources, completions, and errors are tested in `tests/CheatEngine.Mcp.Tests/Hosting/GatewayServerTests.cs`, `GatewayToolCatalogTests.cs`, `GatewayResourceTests.cs`, `GatewayCompletionTests.cs`, and `ErrorCorrelationTests.cs`.
The feature defaults, precedence, validation, and activation boundary are tested in `tests/CheatEngine.Mcp.Tests/Plugin/FeatureOptionsTests.cs`, `McpConfigurationTests.cs`, and `McpModuleLifecycleTests.cs`.
`McpContractRules` validates public naming, annotations, dispatch classes, schemas, and output presence at catalog construction.
`GatewayToolCatalog` adds the required routing argument while preserving every backend field.
`GatewayRouter`, `BackendConnectionPool`, and `InstanceIdentityVerifier` bind a request to one verified activation and report unknown effects without replay.
`McpFeatureGate` and the domain builders carry the four capability requirements into the public catalog and runtime admission path.
`McpEnvironmentConfigurationSource`, `McpOptionsValidators`, and the plugin configuration builder implement the documented precedence and validation boundary.

## Contract freeze gate

The stable contract cannot freeze until partial rows 3.01, 3.03, 3.04, 3.06, 3.07, and 3.09 have reviewed evidence.
Rows that pass for `808a626e65eddab113737a27a7c2345555b1c241` still require the exact RC and final-candidate repetitions named above.
This checkpoint does not qualify any complete native workflow domain, installation path, client, soak, or public release.
