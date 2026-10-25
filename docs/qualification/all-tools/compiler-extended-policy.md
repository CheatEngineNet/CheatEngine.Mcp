# Extended compiler qualification policy

Status: harness preparation only. No native execution is approved by this document.

`compiler-extended` may be selected only after the existing two compiler acknowledgements and a reviewed policy revision. It adds fixed, non-operator-controlled variants:

* private-copy `CSCompiler.dll` quarantine after official-source identity preflight, to observe unavailable/prerequisite behavior without changing the installed CE copy;
* shared run-owned TEMP topology, to observe B shutdown and a reviewed plugin reload; and
* a one-shot bounded post-`compileCS` hold that writes an owned ready receipt, waits at most ten seconds for an owned release file, then returns the original values unchanged. The harness may stop B while the production `exec_compile_csharp` call is held, then release in `finally`. Cancellation, timeout, missing release, or an unexpected file is a stop condition and never retries compilation.

The hold bridge accepts a fixed `armHold` command and waits for a fixed release file; neither can choose source, paths, target, or process. It never deletes, renames, or copies CE output.

`compiler-injection` remains disabled pending a third exact acknowledgement and separate review. A fixed .NET Framework fixture must publish hash, PID, architecture, and CLR identity before one fixed `exec_inject_dotnet` call may be considered. The existing .NET 10 target is not evidence for that prerequisite.
