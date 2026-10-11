# CheatEngine.Mcp 2.0.0-beta.3

This Windows x64 prerelease adds the MCP roadmap work since beta.2. Download `CheatEngine.Mcp-2.0.0-beta.3-win-x64.zip` and verify it with `SHA256SUMS.txt`. The ZIP contains the single Cheat Engine plugin DLL, the matching gateway executable, installation instructions, and licenses.

## Changes since beta.2

- Added bounded pointer access analysis and pointer-chain validation, plus an explicit C# compile-and-export tool. Compilation does not inject the result.
- Added prepared module, region, thread, symbol, and breakpoint navigation resources. Call the corresponding explicit enumeration tool first; stale or target-mismatched snapshots are refused. Full CE enumeration can still block regardless of result limits.
- Bounded structure lookup and element paging, fixed thread-list ownership, and retained failed pointer-search cleanup reservations.
- Improved effect metadata, unavailable-field handling, error correlation, packaged catalog checks, and Debug/Release portable CI coverage.
- Removed the prerelease exports resource URI; call `module_list_exports` explicitly. Clients should handle omitted unavailable fields and prepare navigation snapshots before reading them.

## Support and known limits

The plugin runs in Cheat Engine 7.7 x64 and needs the x64 .NET 10, ASP.NET Core 10, and Windows Desktop 10 runtimes. The x64 gateway is self-contained. The four optional execution capabilities remain enabled by default; configure their exposure for your own setup. See the [support matrix](https://github.com/CheatEngineNet/CheatEngine.Mcp/blob/main/docs/qualification/v2.0.0/support-matrix.md) for details.

Portable tests, package checks, and the gateway smoke test do not establish live Cheat Engine qualification. An intermittent native Lua dispatch corruption and an empty Settings plugin-list failure remain unresolved. The new standalone navigation/Settings Lua cases and the broader live lifecycle, performance, install/upgrade, and rollback checks have not run on this release candidate. This prerelease is not the stable v2.0.0 sign-off; [qualification status](https://github.com/CheatEngineNet/CheatEngine.Mcp/blob/main/docs/qualification/v2.0.0/release-signoff.md) records the remaining work.

## Upgrade and rollback

Keep a backup of the existing matching plugin and gateway, your configuration, and CE settings. Disable the plugin, close Cheat Engine and the gateway, then install both beta.3 files from this ZIP in a new folder. Replace only this plugin's old CE entry if its path changed, point your MCP client at the new gateway, and discover a fresh `instanceId` with `instance_list`. Do not mix beta.2 and beta.3 files. To roll back, close both processes and restore the previous matching pair and registration. The [recovery guide](https://github.com/CheatEngineNet/CheatEngine.Mcp/blob/main/srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/connection-troubleshooting.md#several-instances-updates-and-removal) covers locked files and partial upgrades.
