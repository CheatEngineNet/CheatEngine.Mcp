# .NET Framework live target fixture

This fixed, disposable target is used only by the separately admitted compiler-injection qualification scenario. It is not part of the normal .NET target project and it is never built or launched by portable tests.

Build either architecture with the fixed script from the repository root:

```powershell
.\eng\Build-LiveFrameworkTarget.ps1 -Architecture x64
.\eng\Build-LiveFrameworkTarget.ps1 -Architecture x86
```

The script requires the installed .NET Framework 4 compiler and writes only below `artifacts/live-framework-target`. The target accepts exactly one manifest path, records its own PID/runtime facts, and exits when its adjacent `.stop` file is created.
