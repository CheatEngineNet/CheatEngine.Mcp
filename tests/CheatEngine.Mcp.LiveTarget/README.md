# CheatEngine.Mcp.LiveTarget

`CheatEngine.Mcp.LiveTarget` is the disposable x64 or explicitly selected x86 process used by live qualification.
It gives the qualification harness a known process, a known writable address, target-owned pointer-chain allocations, and a bounded lifetime, so native checks never need to attach to an unrelated application.

This is a .NET 10 `WinExe` test utility.
It is not shipped with the plugin or gateway and has no production role.

## Runtime behavior

The executable normally requires exactly one argument: the absolute or relative path of a manifest file to create.
The reviewed live-soak harness may instead pass `--soak <manifest-path>`, which selects the fixed 2-hour-15-minute lifetime. It accepts no caller-provided duration or other workload arguments. It exits with code `64` when those forms are missing, blank, or accompanied by other arguments.

At startup, the process owns four 64-byte unmanaged allocations and one pointer-sized allocation.
It writes the signed 32-bit value `20260926` at the direct-value and final pointer-chain addresses.
It writes a JSON manifest through a temporary file in the manifest directory and moves that file into place.

The manifest has this shape:

```json
{
  "processId": 1234,
  "address": "0x7FF600000000",
	"pointerWidth": 64,
	"initialValue": 20260926,
	"pointerRootAddress": "0x7FF600000000",
	"pointerTargetAddress": "0x7FF600000000",
	"zeroPointerRootAddress": "0x7FF600000000",
	"unreadableRootAddress": "0x1",
	"pointerOffsets": ["-10", "20"]
}
```

`processId` identifies the live target process.
`address` is the allocation address in uppercase hexadecimal with a `0x` prefix.
`pointerWidth` is the target process pointer width, either `32` or `64`.
`initialValue` records the original value that the live scenario expects to find.

The pointer root reaches `pointerTargetAddress` through the signed offsets `-10`, then `20`.
`zeroPointerRootAddress` stores a zero pointer so qualification can distinguish a resolved address zero from an unreadable typed final value.
After publishing the manifest, the process polls for a sibling stop marker named `<manifest>.stop`.
It exits successfully when that file appears or when its normal ten-minute lifetime expires. The reviewed `--soak` form uses its fixed 2-hour-15-minute maximum instead. It then releases every owned unmanaged allocation.
It has no window, network listener, input handling, or persistent state beyond the manifest and stop marker.

## Relationship to live qualification

[CheatEngine.Mcp.Tests](../CheatEngine.Mcp.Tests/README.md) builds this project as a dependency with `ReferenceOutputAssembly="false"`.
The live sandbox starts a separate target beside each private Cheat Engine copy, reads its manifest, and uses the stop marker during cleanup.

The live scenario verifies instance isolation while reading and writing the target's known value, scanning for it, creating and removing an address-list record, and restoring any changed speed setting.
The harness owns process startup and shutdown, and it records failures and cleanup results outside the repository.

Run the live scenario only through the documented opt-in flow in [CONTRIBUTING.md](../../CONTRIBUTING.md#live-qualification).
It requires a maintainer-authorized local session with every regular Cheat Engine and DebugView process closed.

## Maintaining the target

Keep this executable deterministic, small, and free of external dependencies.
The normal live fixture uses the x64 executable. An explicitly selected x86 fixture uses the repository-built DLL under `artifacts/live-target-x86` through the installed x86 .NET host; it qualifies only the disposable target's pointer width, while Cheat Engine, the plugin, and gateway remain x64.

Build the x86 fixture before selecting it, then set the bounded target-architecture choice for the live test:

```powershell
dotnet build tests/CheatEngine.Mcp.LiveTarget/CheatEngine.Mcp.LiveTarget.csproj -c Release -p:PlatformTarget=x86 -p:UseAppHost=false -p:ArtifactsPath="$PWD/artifacts/live-target-x86" -p:RestoreLockedMode=true
$env:CHEATENGINE_MCP_LIVE_QUALIFICATION_TARGET_ARCHITECTURE = 'x86'
```

The live report records the selected target architecture. That is evidence for the x86 disposable fixture only; it is not evidence that the x64 Cheat Engine host, plugin, or gateway ran as x86.
Do not add behavior that touches another process, accepts network input, or persists data outside its explicit manifest and stop marker.

If the manifest schema, initial value, allocation layout, or lifetime changes, update the corresponding parsing and assertions in `LiveSandboxSession` and `McpLiveQualificationTests` in the test project.
Run the maintainer-authorized live qualification before relying on that change as native-host verification.
