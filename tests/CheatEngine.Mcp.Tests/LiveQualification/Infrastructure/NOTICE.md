# Live-test infrastructure attribution

These six C# helpers are adapted from the MIT-licensed CheatEngine.Client test suite at commit `f88de3d843252c9139f08c71531a02f03c0516bb`:

- `CheatEngineInstallation.cs`
- `CheatEngineRegistryGuard.cs`
- `CheatEngineUserState.cs`
- `DebugOutputCapture.cs`
- `RegistrySnapshot.cs`
- `SandboxLayout.cs`

Source: https://github.com/CheatEngineNet/CheatEngine.Client/tree/f88de3d843252c9139f08c71531a02f03c0516bb/tests/CheatEngine.Client.Tests/LiveQualification

Copyright (c) 2026 AriusII, ShadowNineX and CheatEngine.Client contributors.
The full license is in `licenses/CheatEngine.Client.LICENSE` at the repository root.

Only the namespace and attribution header were changed when importing these helpers. They preserve the installation-copy, debug-capture, settings-backup, and crash-recovery behavior used by this repository's live tests. The path predicate they require lives in the MCP test opt-in policy. These are test support files, not a source build of the Client library; runtime Client assemblies come exclusively from NuGet.
