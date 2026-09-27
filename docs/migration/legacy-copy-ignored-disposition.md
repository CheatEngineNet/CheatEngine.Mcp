# Ignored-output disposition for the legacy copy

This note records the Git-ignored files found in `D:\CheatEngine\CheatEngine.Mcp - Copie` at the closing inventory.
The detailed file names, sizes and SHA-256 values are in [legacy-copy-ignored.sha256](legacy-copy-ignored.sha256).

| Source group | Files | Bytes | Disposition | Reason |
|---|---:|---:|---|---|
| `.idea/.idea.CeMCP/.idea/projectSettingsUpdater.xml` and `workspace.xml` | 2 | 9,535 | Excluded | Rider local IDE state; no product capability or documentation. |
| `artifacts/bin/` | 827 | 189,461,125 | Excluded | Generated Debug and Release assemblies, dependencies and test hosts from the legacy build. |
| `artifacts/obj/` | 292 | 4,612,134 | Excluded | Generated MSBuild intermediates and source-generator output. |
| `artifacts/plugin/` | 34 | 4,961,237 | Excluded | Generated legacy plugin package output. |

The four groups total 1,155 files and 199,044,031 bytes.
The generated files contain no source-tree or documentation path.
Their disposition does not affect the v2 capability mapping, which remains in [migration-registry.md](../migration-registry.md).
