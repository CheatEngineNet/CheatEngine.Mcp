param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repoRoot
try {
    dotnet build src/CheatEngine.Mcp.Bootstrap -c $Configuration -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw 'Plugin bundle build failed.' }
    dotnet publish src/CheatEngine.Mcp.Gateway -c $Configuration -p:PublishProfile=Standalone -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw 'Standalone gateway publish failed.' }
    $variant = $Configuration.ToLowerInvariant()
    $dist = Join-Path $repoRoot "artifacts/dist/$variant"
    [IO.Directory]::CreateDirectory($dist) | Out-Null
    $expected = @('CheatEngine.Mcp.dll', 'CheatEngine.Mcp.Gateway.exe', 'skills')
    $unexpected = @(Get-ChildItem -LiteralPath $dist | Where-Object { $_.Name -notin $expected -or $_.PSIsContainer -ne ($_.Name -eq 'skills') })
    if ($unexpected.Count -ne 0) { throw "Unexpected files in distribution folder $dist. Move them before publishing." }
    Copy-Item -LiteralPath (Join-Path $repoRoot "artifacts/bin/CheatEngine.Mcp.Bootstrap/$variant/CheatEngine.Mcp.dll") -Destination $dist
    Copy-Item -LiteralPath (Join-Path $repoRoot "artifacts/publish/CheatEngine.Mcp.Gateway/$variant-standalone/CheatEngine.Mcp.Gateway.exe") -Destination $dist
    # The operator skill is installed by the AI client and is never part of the plugin payload.
    $skillSource = Join-Path $repoRoot 'skills/cheatengine-mcp'
    $skillDestination = [IO.Path]::GetFullPath((Join-Path $dist 'skills/cheatengine-mcp'))
    $distRoot = [IO.Path]::GetFullPath($dist) + [IO.Path]::DirectorySeparatorChar
    if (-not $skillDestination.StartsWith($distRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Skill output must remain inside the distribution directory.' }
    if (Test-Path -LiteralPath $skillDestination) {
        $resolvedSkillDestination = (Resolve-Path -LiteralPath $skillDestination).Path
        if (-not $resolvedSkillDestination.StartsWith($distRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing skill cleanup outside the distribution directory.' }
        Remove-Item -LiteralPath $resolvedSkillDestination -Recurse -Force
    }
    foreach ($file in Get-ChildItem -LiteralPath $skillSource -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($skillSource, $file.FullName)
        if ($file.Name -in @('local-cheat-engine.md', '.gitignore')) { continue }
        $destination = Join-Path $skillDestination $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    Get-ChildItem -LiteralPath $dist | Select-Object Name, Length
}
finally { Pop-Location }
