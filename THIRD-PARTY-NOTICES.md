# Third-party notices

CheatEngine.Mcp is licensed under the MIT License (see `LICENSE`). Its distribution also redistributes the third-party
components listed below. This file and the `licenses/` folder ship twice: in the plugin folder (`CheatEngine.Mcp/`) and
beside the gateway executable (`CheatEngine.Mcp.Gateway.exe`).

## NuGet packages

Each `package` library of the plugin and gateway `deps.json` files has one row. `ThirdPartyNoticesTests` fails when a
shipped package has no row.

| Package | Version | License | Copyright | Shipped in | License text |
|---|---|---|---|---|---|
| CheatEngine.Client | 1.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors | Metapackage, no files | `licenses/CheatEngine.Client.LICENSE` |
| CheatEngine.Client.Abstractions | 1.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors | Plugin folder, gateway | `licenses/CheatEngine.Client.LICENSE` |
| CheatEngine.Client.Core | 1.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors | Plugin folder, gateway | `licenses/CheatEngine.Client.LICENSE` |
| CheatEngine.Client.Extensions.DependencyInjection | 1.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors | Plugin folder, gateway | `licenses/CheatEngine.Client.LICENSE` |
| CheatEngine.Client.Fluent | 1.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors | Plugin folder, gateway | `licenses/CheatEngine.Client.LICENSE` |
| CheatEngine.Client.Hosting | 1.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors | Plugin folder, gateway | `licenses/CheatEngine.Client.LICENSE` |
| CheatEngine.SDK | 2.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.SDK contributors | Plugin folder: the seven `CheatEngine.SDK*.dll` assemblies and the native `cheatengine-sdk-lua-bridge.dll`. Gateway: the managed assemblies only | `licenses/CheatEngine.SDK.LICENSE` |
| Microsoft.Extensions.AI.Abstractions | 10.10.1 | MIT | Copyright (c) .NET Foundation; © Microsoft Corporation | Plugin folder, gateway | `licenses/Microsoft.Extensions.AI.LICENSE` |
| ModelContextProtocol | 2.2.0 | Apache-2.0 | © Model Context Protocol a Series of LF Projects, LLC. | Plugin folder, gateway | `licenses/ModelContextProtocol.LICENSE`, `licenses/ModelContextProtocol.THIRD-PARTY-NOTICES.txt` |
| ModelContextProtocol.AspNetCore | 2.2.0 | Apache-2.0 | © Model Context Protocol a Series of LF Projects, LLC. | Plugin folder, gateway | `licenses/ModelContextProtocol.LICENSE`, `licenses/ModelContextProtocol.THIRD-PARTY-NOTICES.txt` |
| ModelContextProtocol.Core | 2.2.0 | Apache-2.0 | © Model Context Protocol a Series of LF Projects, LLC. | Plugin folder, gateway | `licenses/ModelContextProtocol.LICENSE`, `licenses/ModelContextProtocol.THIRD-PARTY-NOTICES.txt` |

Notes:

- The ModelContextProtocol license file is the upstream one. The project is moving from MIT to Apache-2.0: new
  contributions are Apache-2.0, and earlier contributions whose authors have not agreed to relicense stay MIT
  (Copyright (c) 2024-2026 Model Context Protocol a Series of LF Projects, LLC.). The file contains both texts.
  Upstream has no `NOTICE` file.
- The upstream ModelContextProtocol third-party notices ship unchanged. They cover mcpdotnet (MIT, Copyright (c) 2024
  Peder Holdgaard Pedersen) and Polyfills (MIT, Copyright (c) .NET Foundation and Contributors). They also list the URI
  Template Tests (Apache-2.0). Those tests are test data only and are not in the packages.
- The upstream third-party notices of Microsoft.Extensions.AI.Abstractions list no components, so only its license
  ships.

## .NET runtime in the gateway

The gateway is a self-contained executable. It includes the .NET runtime and the ASP.NET Core shared framework, taken
from the win-x64 runtime packs of the SDK pinned in `global.json`. The plugin folder contains neither: Cheat Engine
hosts the plugin on the .NET runtimes installed on the machine.

| Framework | Version | License | Copyright | Shipped in | License text |
|---|---|---|---|---|---|
| Microsoft.NETCore.App | 10.0.12 | MIT, plus the notices of the components it includes | Copyright (c) .NET Foundation and Contributors | Gateway only | `licenses/Microsoft.NETCore.App/LICENSE.TXT`, `licenses/Microsoft.NETCore.App/THIRD-PARTY-NOTICES.TXT` |
| Microsoft.AspNetCore.App | 10.0.12 | MIT, plus the notices of the components it includes | Copyright (c) .NET Foundation and Contributors | Gateway only | `licenses/Microsoft.AspNetCore.App/LICENSE.txt`, `licenses/Microsoft.AspNetCore.App/THIRD-PARTY-NOTICES.TXT` |

When the pinned SDK changes the runtime version, update this table. Then copy `LICENSE.TXT` (or `LICENSE.txt`) and
`THIRD-PARTY-NOTICES.TXT` again from the matching `microsoft.netcore.app.runtime.win-x64` and
`microsoft.aspnetcore.app.runtime.win-x64` packages in the NuGet cache.

## Not redistributed

- Cheat Engine, and the .NET 10 runtimes it needs to host the plugin (Microsoft.NETCore.App, Microsoft.AspNetCore.App
  and Microsoft.WindowsDesktop.App). The user installs them.
- Build-only and test-only packages: analyzers, source generators, the test framework and its extensions. They never
  reach the plugin folder or the gateway.

## Where the texts come from

| File | Source |
|---|---|
| `licenses/CheatEngine.Client.LICENSE` | `LICENSE` of CheatEngine.Client at `f88de3d843252c9139f08c71531a02f03c0516bb`, the source commit of the 1.0.0 packages |
| `licenses/CheatEngine.SDK.LICENSE` | `LICENSE` of CheatEngine.SDK at `325c47b573f8bd39a247f1d0101f110fa36c1696`, the source commit of the 2.0.0 package |
| `licenses/Microsoft.Extensions.AI.LICENSE` | `LICENSE` of dotnet/extensions at `59e1741f6fb45cf7ae08e2df088aef71537a7bc4`, the source commit of the 10.10.1 package |
| `licenses/ModelContextProtocol.LICENSE` | `LICENSE` of modelcontextprotocol/csharp-sdk at `6fa3825973949a9c4f0cd8af344e15a8db09dc35`, the source commit of the 2.2.0 packages |
| `licenses/ModelContextProtocol.THIRD-PARTY-NOTICES.txt` | `THIRD-PARTY-NOTICES.txt` of modelcontextprotocol/csharp-sdk at the same commit |
| `licenses/Microsoft.NETCore.App/*` | The Microsoft.NETCore.App.Runtime.win-x64 10.0.12 package |
| `licenses/Microsoft.AspNetCore.App/*` | The Microsoft.AspNetCore.App.Runtime.win-x64 10.0.12 package |
