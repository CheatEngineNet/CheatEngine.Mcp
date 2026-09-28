# CheatEngine.Mcp.LiveTarget

`CheatEngine.Mcp.LiveTarget` is the disposable x64 process used by live qualification.
It gives the qualification harness a known process, a known writable address, and a bounded lifetime, so native checks never need to attach to an unrelated application.

This is a .NET 10 `WinExe` test utility.
It is not shipped with the plugin or gateway and has no production role.

## Runtime behavior

The executable requires exactly one argument: the absolute or relative path of a manifest file to create.
It exits with code `64` when that argument is missing, blank, or accompanied by extra arguments.

At startup, the process allocates 64 bytes of unmanaged memory and writes the signed 32-bit value `20260926` at the allocation's first address.
It writes a JSON manifest through a temporary file in the manifest directory and moves that file into place.

The manifest has this shape:

```json
{
  "processId": 1234,
  "address": "0x7FF600000000",
  "initialValue": 20260926
}
```

`processId` identifies the live target process.
`address` is the allocation address in uppercase hexadecimal with a `0x` prefix.
`initialValue` records the original value that the live scenario expects to find.

After publishing the manifest, the process polls for a sibling stop marker named `<manifest>.stop`.
It exits successfully when that file appears or when its ten-minute lifetime expires, then releases the unmanaged allocation.
It has no window, network listener, input handling, or persistent state beyond the manifest and stop marker.

## Relationship to live qualification

[CheatEngine.Mcp.Tests](../CheatEngine.Mcp.Tests/README.md) builds this project as a dependency with `ReferenceOutputAssembly="false"`.
The live sandbox starts a separate target beside each private Cheat Engine copy, reads its manifest, and uses the stop marker during cleanup.

The live scenario verifies instance isolation while reading and writing the target's known value, scanning for it, creating and removing an address-list record, and restoring any changed speed setting.
The harness owns process startup and shutdown, and it records failures and cleanup results outside the repository.

Run the live scenario only through the documented opt-in flow in [CONTRIBUTING.md](../../CONTRIBUTING.md#live-qualification).
It requires a maintainer-authorized local session with every regular Cheat Engine and DebugView process closed.

## Maintaining the target

Keep this executable deterministic, small, x64-only, and free of external dependencies.
Do not add behavior that touches another process, accepts network input, or persists data outside its explicit manifest and stop marker.

If the manifest schema, initial value, allocation layout, or lifetime changes, update the corresponding parsing and assertions in `LiveSandboxSession` and `McpLiveQualificationTests` in the test project.
Run the maintainer-authorized live qualification before relying on that change as native-host verification.
