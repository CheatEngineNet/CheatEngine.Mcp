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
    $expected = @('CheatEngine.Mcp.dll', 'CheatEngine.Mcp.Gateway.exe')
    $unexpected = @(Get-ChildItem -LiteralPath $dist | Where-Object { $_.PSIsContainer -or $_.Name -notin $expected })
    if ($unexpected.Count -ne 0) { throw "Unexpected files in distribution folder $dist. Move them before publishing." }
    Copy-Item -LiteralPath (Join-Path $repoRoot "artifacts/bin/CheatEngine.Mcp.Bootstrap/$variant/CheatEngine.Mcp.dll") -Destination $dist
    Copy-Item -LiteralPath (Join-Path $repoRoot "artifacts/publish/CheatEngine.Mcp.Gateway/$variant-standalone/CheatEngine.Mcp.Gateway.exe") -Destination $dist
    Get-ChildItem -LiteralPath $dist | Select-Object Name, Length
}
finally { Pop-Location }
