# Stable v2.0.0 contract checkpoint

Date: 2026-10-09.
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
| 3.03 | source audit and standalone fixed-Lua regressions complete; exact-candidate and CE evidence pending | The full-domain review in offline-contract-audit.md covers unavailable observations, zero/empty results, raw collector conventions and gateway forwarding. All 626 Release NativeLua cases passed after narrow user authorization. | Retain portable/schema/standalone/native results on the exact candidate; do not infer installed-collector parity or real CE behavior from public source and stubbed host APIs. |
| 3.04 | portable client audit complete; final candidate pending | The 12 `ExactValuePipelineTests` cases cover maximum 64-bit addresses and pointers, both signed 64-bit extremes, unsigned values above JavaScript's safe range, and negative hexadecimal offsets through a real .NET MCP client and gateway. | Retain these regressions in the clean RC and final stable checks; this is not qualification of named interactive clients or real target memory. |
| 3.05 | pass for the clean tested candidate | `GatewayToolCatalogTests.RoutedTools_InstanceId_IsTheFirstPropertyAndRequirement`, `GatewayServerTests.CallTool_TwoPublishedInstances_RoutesOnlyToRequestedBackendAndStripsInstanceId`, `GatewayServerTests.CallTool_MissingInstanceId_ReturnsInvalidArgumentEnvelope`, `GatewayServerTests.CallTool_UnknownInstanceId_ReturnsInstanceUnavailableNotStarted`, and `GatewayServerTests.CallTool_StaleIdentity_ReturnsInstanceUnavailableNotStartedAndNeverForwards` cover explicit routing and disappearance. | Repeat portable V+ and the maintained two-instance VL on the exact final package. |
| 3.06 | source audit complete; candidate validation pending | The inventory review covers all 193 backend tools. Six further metadata corrections supplement the earlier eight, and the slow exports resource projection is removed while the explicit tool remains. See offline-contract-audit.md. | Validate final snapshots, descriptions and packaged reflection on the exact candidate; native dispatch timing remains a separate qualification gate. |
| 3.07 | source audit, selected portable cases and standalone hint regression complete; real CE and final candidate pending | Existing gate tests plus the new table, secondary AA, mixed record batch and process-open tests below cover refusal before effects. `record_set_script` now declares its fixed AA requirement and refuses before dispatch; the `record_clear` recovery-hint assertion passed in the approved standalone NativeLua suite. | Verify actual CE table/script hooks when permitted and retain the complete portable/standalone/native evidence on the final candidate. |
| 3.08 | pass at documentation level | `McpConfigurationTests.Load_Defaults_AreLoopbackAndAllExecutionIsEnabled`, `McpConfigurationTests.Load_PluginUserAndEnvironment_UseDocumentedPrecedence`, README configuration text, `support-matrix.md`, and `acceptance.md` agree on enabled switches, empty roots, precedence, and exposure semantics. | Reconcile these statements against the exact RC configuration and final release notes. |
| 3.09 | source and portable audit complete; final candidate pending | Existing `ContractJsonTests` and gateway protocol-error tests remain applicable. `ErrorCorrelationTests.CallTool_EveryHostEffect_PreservesCorrelatedEnvelopeThroughGateway` adds direct and routed coverage for all six host effects, exact envelope equality, hint, details, retryability and the logged correlation ID. Non-object context and invalid reserved IDs have explicit regressions. | Include the reviewed correlation correction in a clean candidate; repeat portable and packaged checks on the exact RC and final source before checking the roadmap's final-candidate criterion. |
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

The stable contract cannot freeze until partial rows 3.01, 3.03 and 3.06 have complete reviewed evidence, native portions of 3.07 are verified, and the working-tree 3.04, 3.07 and 3.09 changes are validated on the clean candidate.
Rows that pass for `808a626e65eddab113737a27a7c2345555b1c241` still require the exact RC and final-candidate repetitions named above.
This checkpoint does not qualify any complete native workflow domain, installation path, client, soak, or public release.

## Error-envelope follow-up

An offline source audit found that an internal error with scalar or array details received a logged correlation ID but omitted that ID from the response.
`McpErrorCorrelation` now keeps non-object JSON context under `details.value` beside `errorId`, without numeric conversion.
It also replaces a non-string reserved `errorId` instead of emitting duplicate keys.
Object context and existing string IDs are preserved, other error kinds are unchanged, and gateway forwarding preserves the complete backend envelope.

| Requirement | Portable evidence |
| --- | --- |
| Preserve array, string, exact large integer, boolean and null context with the logged ID. | `ErrorCorrelationTests.CallTool_NonObjectInternalDetails_KeepsValueAndReturnsTheLoggedErrorId` |
| Replace an invalid reserved ID while retaining the other object properties. | `ErrorCorrelationTests.Correlate_NonStringExistingId_ReplacesItWithoutDuplicateKeys` |
| Preserve every host-effect class, operation, retryability, hint, details and correlation through direct MCP and gateway calls. | `ErrorCorrelationTests.CallTool_EveryHostEffect_PreservesCorrelatedEnvelopeThroughGateway` |

The 22 focused correlation cases passed in Release, and independent Astra source review found no remaining actionable findings after documentation corrections.
The previous and current full-suite results are recorded separately in [remaining-checkpoint.md](remaining-checkpoint.md).
These are portable source/filter/routing checks with controlled backends, not evidence that a real CE operation produced every host effect.

The latest offline package refresh also verifies the working-tree contract in the actual Debug and Release single-DLL distributions and Native AOT gateway executables. Both packaged plugins match all backend catalog snapshots in two isolated contexts each; both AOT executables pass the existing no-instance initialization/catalog/knowledge test. Exact binary hashes, same-input ZIP reproduction, extraction checks and the uncommitted-source boundary are recorded in [remaining-checkpoint.md](remaining-checkpoint.md#offline-package-refresh---2026-10-09). These current local package results do not replace the required clean RC/final stable repetitions or native qualification.

## Exact values, gates and effect metadata follow-up

The 2026-10-09 continuation adds actual MCP serialization and loopback gateway forwarding around controlled Client doubles.
The gateway's controlled backend returns the direct tool response, so the check proves serialization and forwarding without claiming another target read.
`MemoryRead_ExactAndEmptyValues_RemainStringsThroughClientAndGateway` covers signed extrema, `9007199254740993`, unsigned maximum, pointer maximum, address maximum, zero and empty text.
`PointerReadChain_SignedOffsetsAndWideAddresses_RoundTripWithoutNumericConversion` covers `0`, `-1` and `-8000000000000000` offsets without numeric coercion.
All 12 focused cases passed in Release; both full portable suites at that earlier checkpoint passed 4,126 tests. The latest full offline audit passed 4,135 tests in each configuration, as recorded in [remaining-checkpoint.md](remaining-checkpoint.md).

| Gate boundary | New portable evidence |
| --- | --- |
| Secondary Lua, target-code and kernel requirements before AA apply. | `CodeAsmTableV2Tests.Apply_SecondaryCapabilityDisabled_RefusesBeforeClientMutation` |
| Loaded XML AA, Mono and kernel content; each opaque-table gate; current-table Mono before load. | `Load_GatedTableContent_DisabledFeatureRefusesBeforeClientLoad`, `Load_OpaqueTable_DisabledExposureGate_RefusesBeforeClientLoad`, `Load_CurrentTableUsesMonoWithCodeExecutionDisabled_ProbesButRefusesBeforeClientLoad` |
| A mixed create/delete batch containing a gated AA record cannot mutate earlier records. | `RecordToolsTests.Create_BatchContainingAutoAssemblerWithoutGate_RefusesBeforeCreatingAnyRecord`, `Delete_BatchContainingActiveAutoAssemblerWithoutGate_RefusesBeforeDeletingAnyRecord` |
| A fixed script-editing requirement rejects before dispatch. | `RecordToolsTests.SetScript_AutoAssemblerWithoutGate_RefusesBeforeAnyDispatch` |
| Mono auto-attach on process creation or file-open cannot reach the open script while disabled. | `ProcessMonoAutoAttachTests.CreateOrOpenFile_MonoAutoAttachWithCodeExecutionOff_RefusesBeforeTheOpenScript` |

The XML fixtures explicitly verify inspection, so a malformed/protected-table fallback cannot masquerade as content-classification coverage.
Focused Release results were 42 assembly/table tests, 177 record tests and 10 process-auto-attach tests, all passing.
The source audit traced the declarative filter, fixed-Lua scanner, table inspector, AA classifier, record mutations, Mono auto-attach, debugger-interface choices, kernel symbol sources, Exec/C# and Speedhack admission paths.
This remains the documented exposure-control boundary: address expressions and stored AA scripts do not acquire sandbox guarantees or new secondary gate promises.
The `record_clear` fixed-Lua hint assertion subsequently passed in the explicitly authorized standalone Release NativeLua suite; real CE hook behavior remains unqualified.

Effect metadata now marks `record_create` and `table_save` destructive because an initial value or explicit overwrite can replace existing data.
`record_set_script` publishes `auto_assembler` in its fixed requirements.
`asm_release_patch`, `record_set_active`, `record_delete`, `record_clear` and `runtime_release_resources` publish `may_prompt`: original AA sections can block or show a dialog, including when called through bulk resource cleanup.
Independent Astra review cleared these scoped source/test changes after the identified corrections. It did not audit every remaining tool or qualify a native runtime.

The .NET and Mono fallback review used public upstream commit `ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37`, separately from installed CE `7.7.1.10828`.
[Mono field enumeration](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/bin/autorun/monoscript.lua#L2159-L2205) reads every returned offset; the collector writes it for Mono and its IL2CPP binding uses `il2cpp_field_get_offset`.
The [.NET Lua field wrapper](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/luadotnetpipe.pas#L278-L337) emits every decoded `Offset`, and its [parameter wrapper](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/luadotnetpipe.pas#L210-L245) emits `CType` for each returned entry.
The [parameter decoder](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/dotnetpipe.pas#L493-L561) preallocates the result array and skips assignment for the explicit failure sentinel without compacting it; the Lua wrapper still emits every slot.
Consequently, a present `CType` can be default metadata from a failed parameter lookup. Its presence does not prove that meaningful metadata was reported.
Offset/property emission alone does not justify changing the public shape to nullable; changing the managed type would not recover failure information already lost upstream. Raw collector values remain preserved under the clarified contract below.
It does not establish parity with the installed collector or complete the remaining domain audit, and no collector was executed.

## .NET parameter metadata correction

The pinned collector calls `IMetaDataImport::GetParamProps` and copies `pdwCPlusTypeFlag` into `CType`.
Microsoft's [runtime implementation](https://github.com/dotnet/runtime/blob/9fc5e2dd35febb732f16020969b43da7933d326b/src/coreclr/md/compiler/import.cpp#L2764-L2793) obtains this code from the parameter's Constant metadata: it returns `ELEMENT_TYPE_VOID` when there is no constant, otherwise the constant's element type.
This is not the declared parameter type. The prior public description and example incorrectly suggested otherwise.

`DotNetParameter.ElementType` retains the raw integer for compatibility, but now identifies it as this metadata-constant code; `ElementTypeName` names only that code.
The changed portable test preserves a declared `Game.Player` signature alongside `1` / `Void` for a parameter without a constant, and retains a zero-initialized unavailable entry without inventing a type name.
`0` can mean unavailable metadata; it must not be interpreted as a declared type. The collector does not retain enough information to reconstruct the original failure.

The [ECMA-335 Param table](https://ecma-international.org/wp-content/uploads/ECMA-335_6th_edition_june_2012.pdf) permits missing rows and sequence `0` for return metadata.
The [runtime enumeration](https://github.com/dotnet/runtime/blob/9fc5e2dd35febb732f16020969b43da7933d326b/src/coreclr/md/compiler/import.cpp#L574-L625) does not filter those rows; the pinned CE implementation enumerates once into a 30-entry buffer, sorts by sequence and discards sequence before returning Lua entries.
The public array therefore promises collector order, potentially including return metadata, placeholders or an incomplete list, and its `index` is the returned position rather than a declared ordinal.
The optional `signature` remains the returned representation of declared types. No signature parser or inferred types were added.
The tool, output schema, knowledge guide and workflow use this same distinction; native collector parity remains unverified.
