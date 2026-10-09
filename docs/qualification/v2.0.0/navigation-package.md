# Local navigation packages

Status: local packaging and independent Astra high evidence review passed for the [first navigation repair](navigation-preparation.md). Subsequent thread/symbol/breakpoint/structure repairs are not included in this package. This is an isolated development payload, not the final stable candidate or a published release.

The packaging source is `artifacts/candidate-preparation/source-bbea5ef9e63c4b5cb8dca301224b39cb`. All 863 copied source hashes match the reviewed navigation snapshot. Build environment properties disable SCM discovery and explicitly set `RepositoryCommit` and `SourceRevisionId` to `0f722ef7d6b37eee0985073dfe594c6559b31c14`; all differences from that base remain separately recorded in the source inventory. Source identity alone does not represent these uncommitted changes.

The maintained `eng/Publish.ps1` completed a locked Release publish. Its probe cold-loaded the exact lone DLL into two isolated contexts and compared both reflected catalogs with the reviewed goldens: 193 backend tools, 43 resources, 14 templates and 43 prompts. The bridge check loaded the ordinary embedded native bridge and inspected its export; it did not invoke Lua operations or attach to a target. The maintained Native AOT gateway smoke completed initialization and tool/resource/template/prompt listing against a fresh empty instance registry.

`eng/Release.ps1` produced two byte-identical ZIPs in separate output directories. The maintained staging script verified the ZIP checksum, exact five-file flat inventory, per-file extraction hashes and matching plugin/gateway version identity. No installed deployment was replaced, no CE process or target was launched, and no release was uploaded.

| Artifact | SHA-256 |
| --- | --- |
| Plugin DLL | `964AB04832035031FA07BF4EBD1D6A8CE8BA743754C0077CABF36F92D958DFDD` |
| Native AOT gateway | `DDB7CEC18D354F911B0F2CBABFC5C84B3F2C9A0A71AEC01A6E2F17B975758971` |
| ZIP, 17,759,980 bytes | `0A5056CC8B482F959DA4F7C150D3B0112F0B46F7CC509DC08A44D4EE516A1342` |

Exact paths, logs, source hashes and extracted inventory are in `artifacts/candidate-preparation/package-evidence.json`. The development version remains `2.0.0-beta.2+0f722ef7d6b37eee0985073dfe594c6559b31c14`; this changed local payload must not replace published beta.2 binaries. Final version/commit identity, later repairs, clean-candidate checks, live qualification and publication remain outstanding.

The [follow-up](navigation-followup.md) also corrects the explicit preparation tools' dispatch metadata. The first package predates that correction; the later package below includes it.

## Latest reviewed follow-up package

The 867-file reviewed follow-up was copied with exact hash verification to `artifacts/candidate-preparation/followup-aa947ac543104f65954bcee7297bbb63`. The same maintained locked Release publish succeeded, including both isolated exact-DLL catalog checks (193 tools, 43 resources, 14 templates, 43 prompts) and the Native AOT gateway smoke against an empty registry. Two local release ZIP builds were byte-identical; maintained staging verified all five files, checksums and plugin/gateway version identity.

| Latest artifact | SHA-256 |
| --- | --- |
| Plugin DLL | `A52F05A8641AE09F0C3F608BCDFF414B4C7B1C62418CAEA220FF2054C01D8DFD` |
| Native AOT gateway | `622FFD85EF21728A8FA04F0BB6A85135EEF41A765BDD3DD30C76138C33498579` |
| ZIP, 17,796,609 bytes | `08208984369011EF8829D8508D57E08657762C86D55B5AE13A1031A03993F3AB` |

Source/file hashes, exact paths and retained log hashes are in `artifacts/candidate-preparation/followup-package-evidence.json`. Independent Astra high package evidence review is scoped clear, including all 867 source hashes, both identical ZIPs, extracted inventories, retained logs and unchanged Settings preparations. This payload includes the repaired prepared-resource metadata and remains a local development version `2.0.0-beta.2+0f722ef7d6b37eee0985073dfe594c6559b31c14`. It cannot replace published beta.2 assets or supply final stable identity evidence. The bridge probe only loaded the ordinary bridge to check exports; no Lua operation, CE host or target was run. New native Lua regressions, integration into primary product source, native qualification and publication remain outstanding.
