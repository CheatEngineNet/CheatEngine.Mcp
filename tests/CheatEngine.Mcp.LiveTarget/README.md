# CheatEngine.Mcp.LiveTarget

The disposable x64 process that live qualification attaches Cheat Engine to.
It owns a known value at a known address, so the live scenario can read, write, freeze and scan real memory without
touching any other program.
It is never shipped and has no purpose outside the tests.

## Behavior

- It takes exactly one argument, the path of a manifest file to write, and exits with code 64 without it.
- It allocates 64 bytes of unmanaged memory and writes the 32-bit value `20260926` at its start.
- It writes the manifest atomically, as JSON with `processId`, `address` (hexadecimal with `0x`) and `initialValue`.
- It then waits, polling every 50 ms, until a file named `<manifest>.stop` appears or 10 minutes pass, frees the memory
  and exits with code 0.
- It is a windowless executable (`WinExe`) and does nothing else: no network, no input, no other files.

## Run

The test project builds it into `artifacts/bin/CheatEngine.Mcp.LiveTarget/<configuration>/`, and the live qualification
harness in [`tests/CheatEngine.Mcp.Tests/LiveQualification`](../CheatEngine.Mcp.Tests/LiveQualification) starts one
instance per Cheat Engine copy and stops it through the stop file.
To run live qualification, follow [Live qualification](../../CONTRIBUTING.md#live-qualification); never start it by
hand next to a real Cheat Engine session.

## Rules

- Keep it small, deterministic and harmless: live tests may only ever attach to this target.
- A change to its value, manifest or lifetime must be matched in `LiveSandboxSession` and the live scenario.
- It stays x64, like the plugin and Cheat Engine.
