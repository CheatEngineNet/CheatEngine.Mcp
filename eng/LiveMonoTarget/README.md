# Live Mono target fixture

This x64 .NET Framework-compatible fixture is intentionally narrow: it publishes one pinned, sequential primitive-instance probe for controlled Mono tool qualification. It is built only by `eng/Build-LiveMonoTarget.ps1` with the installed Framework64 `csc.exe`; it embeds no runtime or package dependency.

The private runtime is the approved extracted official Mono `6.12.0.206` x64 distribution at `artifacts/fixtures/mono-6.12.0.206-x64/extracted/runtime`. Native `mono.exe --version` was separately observed as amd64/sgen. The shipped `mscorlib.dll` IL and `object.h` agree that `GCHandle.ToIntPtr` exposes the legacy signed 32-bit handle field as native-int bits and that `mono_gchandle_get_target(uint32_t)` returns `MonoObject*` with C calling convention. The target preserves the lower 32 bits with `unchecked((uint)GCHandle.ToIntPtr(handle).ToInt64())`.

`GCHandle.AddrOfPinnedObject` is not used as an object-pointer oracle: it addresses object data/body for supported blittable objects, not the `MonoObject*` header. The fixture keeps the object rooted by both `StaticRoot` and `GCHandleType.Pinned` for no more than 120 seconds.

The manifest has fixed fields only and is atomically written below the supplied absolute owned root. The target does not read commands, paths, or code from it. `StaticAdd42(0)` is the only startup warmup; `InstanceBumpCounter(7)` stays untouched at `Counter == 7000` for the live harness to invoke exactly once and verify `7007`.

This fixture is an input to qualification, not live evidence. No Mono tool is qualified until the private target, MCP calls, restoration, target exit, and host cleanup all pass together.
