# Third-party notices

CheatEngine.Mcp is licensed under the MIT License (see `LICENSE`). Its distribution also redistributes the third-party
components listed below. This file is self-contained: it names each component with its copyright and license, and it
reproduces the license texts at the end. It ships twice, with `LICENSE`: in the plugin folder (`CheatEngine.Mcp/`) and
beside the gateway executable (`CheatEngine.Mcp.Gateway.exe`).

## NuGet packages

Each `package` library of the plugin and gateway `deps.json` files has one row. `ThirdPartyNoticesTests` fails when a
shipped package has no row.

| Package | Version | License | Copyright | Shipped in | License text |
|---|---|---|---|---|---|
| CheatEngine.Client | 1.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors | Metapackage, no files | [MIT License](#mit-license) |
| CheatEngine.Client.Abstractions | 1.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors | Plugin folder, gateway | [MIT License](#mit-license) |
| CheatEngine.Client.Core | 1.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors | Plugin folder, gateway | [MIT License](#mit-license) |
| CheatEngine.Client.Extensions.DependencyInjection | 1.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors | Plugin folder, gateway | [MIT License](#mit-license) |
| CheatEngine.Client.Fluent | 1.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors | Plugin folder, gateway | [MIT License](#mit-license) |
| CheatEngine.Client.Hosting | 1.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors | Plugin folder, gateway | [MIT License](#mit-license) |
| CheatEngine.SDK | 2.0.0 | MIT | Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.SDK contributors | Plugin folder: the seven `CheatEngine.SDK*.dll` assemblies and the native `cheatengine-sdk-lua-bridge.dll`. Gateway: the managed assemblies only | [MIT License](#mit-license) |
| Microsoft.Extensions.AI.Abstractions | 10.10.1 | MIT | Copyright (c) .NET Foundation; © Microsoft Corporation | Plugin folder, gateway | [MIT License](#mit-license) |
| ModelContextProtocol | 2.2.0 | Apache-2.0 (MIT for earlier contributions not yet relicensed) | © Model Context Protocol a Series of LF Projects, LLC. | Plugin folder, gateway | [Apache License 2.0](#apache-license-20), [MIT License](#mit-license) |
| ModelContextProtocol.AspNetCore | 2.2.0 | Apache-2.0 (MIT for earlier contributions not yet relicensed) | © Model Context Protocol a Series of LF Projects, LLC. | Plugin folder, gateway | [Apache License 2.0](#apache-license-20), [MIT License](#mit-license) |
| ModelContextProtocol.Core | 2.2.0 | Apache-2.0 (MIT for earlier contributions not yet relicensed) | © Model Context Protocol a Series of LF Projects, LLC. | Plugin folder, gateway | [Apache License 2.0](#apache-license-20), [MIT License](#mit-license) |

Notes:

- ModelContextProtocol is moving from MIT to Apache-2.0: new contributions are Apache-2.0, and earlier contributions
  whose authors have not agreed to relicense stay MIT (Copyright (c) 2024-2026 Model Context Protocol a Series of LF
  Projects, LLC.). Both texts are reproduced below. Upstream has no `NOTICE` file.
- The upstream ModelContextProtocol third-party notices cover mcpdotnet (MIT, Copyright (c) 2024 Peder Holdgaard
  Pedersen) and Polyfills (MIT, Copyright (c) .NET Foundation and Contributors), both under the MIT License text below.
  They also list the URI Template Tests (Apache-2.0), which are test data only and are not in the packages.
- The upstream third-party notices of Microsoft.Extensions.AI.Abstractions list no components.

## .NET runtime in the gateway

The gateway is a self-contained Native AOT executable. It includes parts of the .NET runtime and of the ASP.NET Core
shared framework, compiled from the win-x64 runtime packs of the SDK pinned in `global.json`. The plugin folder
contains neither: Cheat Engine hosts the plugin on the .NET runtimes installed on the machine.

| Framework | Version | License | Copyright | Shipped in | License text |
|---|---|---|---|---|---|
| Microsoft.NETCore.App | 10.0.12 | MIT, plus the notices of the components it includes | Copyright (c) .NET Foundation and Contributors | Gateway only | [MIT License](#mit-license) and the runtime pack's `THIRD-PARTY-NOTICES.TXT` |
| Microsoft.AspNetCore.App | 10.0.12 | MIT, plus the notices of the components it includes | Copyright (c) .NET Foundation and Contributors | Gateway only | [MIT License](#mit-license) and the runtime pack's `THIRD-PARTY-NOTICES.TXT` |

The component notices of the two frameworks are long and change with every servicing release. They are the
`THIRD-PARTY-NOTICES.TXT` files of the `microsoft.netcore.app.runtime.win-x64` and
`microsoft.aspnetcore.app.runtime.win-x64` 10.0.12 packages, found in the NuGet cache after a restore. When the pinned
SDK changes the runtime version, update this table.

## Not redistributed

- Cheat Engine, and the .NET 10 runtimes it needs to host the plugin (Microsoft.NETCore.App, Microsoft.AspNetCore.App
  and Microsoft.WindowsDesktop.App). The user installs them.
- Build-only and test-only packages: analyzers, source generators, the test framework and its extensions. They never
  reach the plugin folder or the gateway.

## Where the texts come from

| Component | Upstream license file |
|---|---|
| CheatEngine.Client 1.0.0 | <https://github.com/CheatEngineNet/CheatEngine.Client/blob/f88de3d843252c9139f08c71531a02f03c0516bb/LICENSE> (the source commit of the packages) |
| CheatEngine.SDK 2.0.0 | <https://github.com/CheatEngineNet/CheatEngine.SDK/blob/325c47b573f8bd39a247f1d0101f110fa36c1696/LICENSE> (the source commit of the package) |
| Microsoft.Extensions.AI.Abstractions 10.10.1 | <https://github.com/dotnet/extensions/blob/59e1741f6fb45cf7ae08e2df088aef71537a7bc4/LICENSE> (the source commit of the package) |
| ModelContextProtocol 2.2.0 | <https://github.com/modelcontextprotocol/csharp-sdk/blob/6fa3825973949a9c4f0cd8af344e15a8db09dc35/LICENSE> and <https://github.com/modelcontextprotocol/csharp-sdk/blob/6fa3825973949a9c4f0cd8af344e15a8db09dc35/THIRD-PARTY-NOTICES.txt> (the source commit of the packages) |
| .NET runtime packs 10.0.12 | `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` of the runtime packs |

## MIT License

This text applies to every MIT component above, with that component's copyright line in place of
`<copyright holders>`.

```text
MIT License

Copyright (c) <copyright holders>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Apache License 2.0

This text applies to the ModelContextProtocol packages.

```text
                                 Apache License
                           Version 2.0, January 2004
                        http://www.apache.org/licenses/

   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

   1. Definitions.

      "License" shall mean the terms and conditions for use, reproduction,
      and distribution as defined by Sections 1 through 9 of this document.

      "Licensor" shall mean the copyright owner or entity authorized by
      the copyright owner that is granting the License.

      "Legal Entity" shall mean the union of the acting entity and all
      other entities that control, are controlled by, or are under common
      control with that entity. For the purposes of this definition,
      "control" means (i) the power, direct or indirect, to cause the
      direction or management of such entity, whether by contract or
      otherwise, or (ii) ownership of fifty percent (50%) or more of the
      outstanding shares, or (iii) beneficial ownership of such entity.

      "You" (or "Your") shall mean an individual or Legal Entity
      exercising permissions granted by this License.

      "Source" form shall mean the preferred form for making modifications,
      including but not limited to software source code, documentation
      source, and configuration files.

      "Object" form shall mean any form resulting from mechanical
      transformation or translation of a Source form, including but
      not limited to compiled object code, generated documentation,
      and conversions to other media types.

      "Work" shall mean the work of authorship, whether in Source or
      Object form, made available under the License, as indicated by a
      copyright notice that is included in or attached to the work
      (an example is provided in the Appendix below).

      "Derivative Works" shall mean any work, whether in Source or Object
      form, that is based on (or derived from) the Work and for which the
      editorial revisions, annotations, elaborations, or other modifications
      represent, as a whole, an original work of authorship. For the purposes
      of this License, Derivative Works shall not include works that remain
      separable from, or merely link (or bind by name) to the interfaces of,
      the Work and Derivative Works thereof.

      "Contribution" shall mean any work of authorship, including
      the original version of the Work and any modifications or additions
      to that Work or Derivative Works thereof, that is intentionally
      submitted to the Licensor for inclusion in the Work by the copyright
      owner or by an individual or Legal Entity authorized to submit on behalf
      of the copyright owner. For the purposes of this definition, "submitted"
      means any form of electronic, verbal, or written communication sent
      to the Licensor or its representatives, including but not limited to
      communication on electronic mailing lists, source code control systems,
      and issue tracking systems that are managed by, or on behalf of, the
      Licensor for the purpose of discussing and improving the Work, but
      excluding communication that is conspicuously marked or otherwise
      designated in writing by the copyright owner as "Not a Contribution."

      "Contributor" shall mean Licensor and any individual or Legal Entity
      on behalf of whom a Contribution has been received by Licensor and
      subsequently incorporated within the Work.

   2. Grant of Copyright License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      copyright license to reproduce, prepare Derivative Works of,
      publicly display, publicly perform, sublicense, and distribute the
      Work and such Derivative Works in Source or Object form.

   3. Grant of Patent License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      (except as stated in this section) patent license to make, have made,
      use, offer to sell, sell, import, and otherwise transfer the Work,
      where such license applies only to those patent claims licensable
      by such Contributor that are necessarily infringed by their
      Contribution(s) alone or by combination of their Contribution(s)
      with the Work to which such Contribution(s) was submitted. If You
      institute patent litigation against any entity (including a
      cross-claim or counterclaim in a lawsuit) alleging that the Work
      or a Contribution incorporated within the Work constitutes direct
      or contributory patent infringement, then any patent licenses
      granted to You under this License for that Work shall terminate
      as of the date such litigation is filed.

   4. Redistribution. You may reproduce and distribute copies of the
      Work or Derivative Works thereof in any medium, with or without
      modifications, and in Source or Object form, provided that You
      meet the following conditions:

      (a) You must give any other recipients of the Work or
          Derivative Works a copy of this License; and

      (b) You must cause any modified files to carry prominent notices
          stating that You changed the files; and

      (c) You must retain, in the Source form of any Derivative Works
          that You distribute, all copyright, patent, trademark, and
          attribution notices from the Source form of the Work,
          excluding those notices that do not pertain to any part of
          the Derivative Works; and

      (d) If the Work includes a "NOTICE" text file as part of its
          distribution, then any Derivative Works that You distribute must
          include a readable copy of the attribution notices contained
          within such NOTICE file, excluding those notices that do not
          pertain to any part of the Derivative Works, in at least one
          of the following places: within a NOTICE text file distributed
          as part of the Derivative Works; within the Source form or
          documentation, if provided along with the Derivative Works; or,
          within a display generated by the Derivative Works, if and
          wherever such third-party notices normally appear. The contents
          of the NOTICE file are for informational purposes only and
          do not modify the License. You may add Your own attribution
          notices within Derivative Works that You distribute, alongside
          or as an addendum to the NOTICE text from the Work, provided
          that such additional attribution notices cannot be construed
          as modifying the License.

      You may add Your own copyright statement to Your modifications and
      may provide additional or different license terms and conditions
      for use, reproduction, or distribution of Your modifications, or
      for any such Derivative Works as a whole, provided Your use,
      reproduction, and distribution of the Work otherwise complies with
      the conditions stated in this License.

   5. Submission of Contributions. Unless You explicitly state otherwise,
      any Contribution intentionally submitted for inclusion in the Work
      by You to the Licensor shall be under the terms and conditions of
      this License, without any additional terms or conditions.
      Notwithstanding the above, nothing herein shall supersede or modify
      the terms of any separate license agreement you may have executed
      with Licensor regarding such Contributions.

   6. Trademarks. This License does not grant permission to use the trade
      names, trademarks, service marks, or product names of the Licensor,
      except as required for reasonable and customary use in describing the
      origin of the Work and reproducing the content of the NOTICE file.

   7. Disclaimer of Warranty. Unless required by applicable law or
      agreed to in writing, Licensor provides the Work (and each
      Contributor provides its Contributions) on an "AS IS" BASIS,
      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
      implied, including, without limitation, any warranties or conditions
      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
      PARTICULAR PURPOSE. You are solely responsible for determining the
      appropriateness of using or redistributing the Work and assume any
      risks associated with Your exercise of permissions under this License.

   8. Limitation of Liability. In no event and under no legal theory,
      whether in tort (including negligence), contract, or otherwise,
      unless required by applicable law (such as deliberate and grossly
      negligent acts) or agreed to in writing, shall any Contributor be
      liable to You for damages, including any direct, indirect, special,
      incidental, or consequential damages of any character arising as a
      result of this License or out of the use or inability to use the
      Work (including but not limited to damages for loss of goodwill,
      work stoppage, computer failure or malfunction, or any and all
      other commercial damages or losses), even if such Contributor
      has been advised of the possibility of such damages.

   9. Accepting Warranty or Additional Liability. While redistributing
      the Work or Derivative Works thereof, You may choose to offer,
      and charge a fee for, acceptance of support, warranty, indemnity,
      or other liability obligations and/or rights consistent with this
      License. However, in accepting such obligations, You may act only
      on Your own behalf and on Your sole responsibility, not on behalf
      of any other Contributor, and only if You agree to indemnify,
      defend, and hold each Contributor harmless for any liability
      incurred by, or claims asserted against, such Contributor by reason
      of your accepting any such warranty or additional liability.

   END OF TERMS AND CONDITIONS
```
