# CheatEngine.Mcp 2.0.0-beta.4

This Windows x64 prerelease updates the Client/SDK dependencies, fixes two tool adapters and expands automated coverage across the complete MCP catalog. Download `CheatEngine.Mcp-2.0.0-beta.4-win-x64.zip` and verify it with `SHA256SUMS.txt`. The ZIP contains the single plugin DLL, matching gateway executable, installation instructions and licenses.

## Changes since beta.3

- Updated `CheatEngine.Client` to 1.1.0 and `CheatEngine.SDK` to 2.0.1, including regenerated normal and Native AOT dependency locks.
- Fixed `code_get_comments` so its address array reaches Lua as one argument, and fixed `process_list_threads` to use CE's documented zero-based StringList indexer.
- Added catalog-derived routing/refusal checks for every backend tool, positive adapter evidence for all 193 backend tools, and gateway-local discovery checks. New catalog entries must have reviewed adapter-test mappings.
- Expanded standalone Lua adapter tests and execution-argument, conversion/calculation and pointer-file boundary, failure and cleanup tests.
- Added a bounded live workflow scenario that tests target, gateway and host restarts independently of the unresolved plugin reload bridge.
- Preserved the latest test-platform package updates from `main`.

## Validation and known limits

The recorded automated baseline passes 5,314 portable tests in both Debug and Release and 651 standalone Lua tests, with no failures or skips. Combined coverage reaches 94.50% of instrumented handwritten product C# lines, including 94.37% in Tools. This C# percentage does not measure embedded Lua branch coverage or native CE behavior.

The release pipeline checks the isolated bundled DLL, reflected catalog, Native AOT gateway smoke responses, ZIP contents and checksums. Native evidence for 37 backend tools comes from the separately identified private dependency-refresh candidate recorded in the [all-tool qualification report](https://github.com/CheatEngineNet/CheatEngine.Mcp/blob/main/docs/qualification/all-tools/README.md); it does not qualify newly rebuilt release bytes. Another 156 backend tools still need native success fixtures, and same-process plugin reload remains unqualified. The original intermittent dispatcher corruption was contained and investigated, but its root cause is not established. This beta is not stable v2.0.0 sign-off.

## Installation, upgrade and rollback

Use Cheat Engine 7.7 x64 with the x64 .NET 10, ASP.NET Core 10 and Windows Desktop 10 runtimes. The gateway is self-contained. All four optional execution capabilities remain enabled by default; configure their exposure for your setup. The recorded bounded native workflows explicitly disabled them.

Back up the previous matching plugin/gateway pair, configuration and CE settings. Disable the plugin and close CE and the gateway before extracting beta.4 into a new folder. Install `CheatEngine.Mcp.dll`, point the MCP client at the matching `CheatEngine.Mcp.Gateway.exe`, then use `instance_list` to discover a fresh instance ID. Keep both binaries from the same ZIP. To roll back, close both processes and restore the previous matching pair and registration. See the [recovery guide](https://github.com/CheatEngineNet/CheatEngine.Mcp/blob/main/srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/connection-troubleshooting.md#several-instances-updates-and-removal) for locked files and partial upgrades.
