param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repoRoot
try {
    $variant = $Configuration.ToLowerInvariant()
    $dist = Join-Path $repoRoot "artifacts/dist/$variant"
    [IO.Directory]::CreateDirectory($dist) | Out-Null
    $distRoot = [IO.Path]::GetFullPath($dist) + [IO.Path]::DirectorySeparatorChar
    $binRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/bin')) + [IO.Path]::DirectorySeparatorChar

    function Remove-Contained([string] $path, [string] $root) {
        $full = [IO.Path]::GetFullPath($path)
        if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing cleanup outside $root." }
        if (Test-Path -LiteralPath $full) {
            $resolved = (Resolve-Path -LiteralPath $full).Path
            if (-not $resolved.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing cleanup outside $root." }
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
        return $full
    }

    # An interrupted Client deployment can leave its staging folder beside the plugin folder.
    Get-ChildItem -LiteralPath $dist -Directory -Force -Filter '.CheatEngine.Mcp.ceclient-staging-*' |
        ForEach-Object { Remove-Contained $_.FullName $distRoot | Out-Null }
    $folders = @('CheatEngine.Mcp', 'skills', 'licenses')
    $expected = $folders + @('CheatEngine.Mcp.Gateway.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md')
    $unexpected = @(Get-ChildItem -LiteralPath $dist | Where-Object { $_.Name -notin $expected -or $_.PSIsContainer -ne ($_.Name -in $folders) })
    if ($unexpected.Count -ne 0) { throw "Unexpected files in distribution folder $dist. Move them before publishing." }

    # The plugin folder is the CheatEngine.Client staged deployment: it validates the plugin profile (CECLIENT010-016)
    # and copies every top-level *.dll, *.json and *.pdb of the plugin output. Symbols are embedded (DebugType=embedded),
    # so there is no .pdb to copy. Start both folders empty so no stale or foreign file survives an update.
    # ContinuousIntegrationBuild maps source paths to /_/, so no local path reaches the distributed assemblies.
    Remove-Contained (Join-Path $repoRoot "artifacts/bin/CheatEngine.Mcp.Plugin/$variant") $binRoot | Out-Null
    $pluginDestination = Remove-Contained (Join-Path $dist 'CheatEngine.Mcp') $distRoot
    dotnet build srcs/CheatEngine.Mcp.Plugin -c $Configuration -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true "-p:CheatEnginePluginOutputPath=$pluginDestination"
    if ($LASTEXITCODE -ne 0) { throw 'Plugin deployment build failed.' }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md'), (Join-Path $repoRoot 'LICENSE'), (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $pluginDestination
    Copy-Item -LiteralPath (Join-Path $repoRoot 'licenses') -Destination (Join-Path $pluginDestination 'licenses') -Recurse

    dotnet publish srcs/CheatEngine.Mcp.Gateway -c $Configuration -p:PublishProfile=Standalone -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Standalone gateway publish failed.' }
    $gateway = Join-Path $dist 'CheatEngine.Mcp.Gateway.exe'
    Copy-Item -LiteralPath (Join-Path $repoRoot "artifacts/publish/CheatEngine.Mcp.Gateway/$variant-standalone/CheatEngine.Mcp.Gateway.exe") -Destination $gateway -Force
    # The self-contained gateway carries the .NET runtime and the same packages as the plugin: its notices sit beside it.
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'), (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $dist -Force
    $gatewayLicenses = Remove-Contained (Join-Path $dist 'licenses') $distRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'licenses') -Destination $gatewayLicenses -Recurse

    # The operator skill is installed by the AI client and is never part of the plugin folder.
    $skillSource = Join-Path $repoRoot 'skills/cheatengine-mcp'
    Remove-Contained (Join-Path $dist 'skills') $distRoot | Out-Null
    $skillDestination = Join-Path $dist 'skills/cheatengine-mcp'
    foreach ($file in Get-ChildItem -LiteralPath $skillSource -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($skillSource, $file.FullName)
        if ($file.Name -in @('local-cheat-engine.md', '.gitignore')) { continue }
        $destination = Join-Path $skillDestination $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }

    # CE resolves the plugin through its deps.json: every asset it names must sit where the host probes for it.
    $deps = Get-Content -Raw -LiteralPath (Join-Path $pluginDestination 'CheatEngine.Mcp.Plugin.deps.json') | ConvertFrom-Json -AsHashtable
    $required = [Collections.Generic.List[string]]::new()
    foreach ($target in $deps.targets.Values) {
        foreach ($library in $target.Values) {
            if ($library.runtime) { $library.runtime.Keys | ForEach-Object { $required.Add([IO.Path]::GetFileName($_)) } }
            if ($library.native) { $library.native.Keys | ForEach-Object { $required.Add([IO.Path]::GetFileName($_)) } }
            if ($library.resources) { $library.resources.GetEnumerator() | ForEach-Object { $required.Add((Join-Path $_.Value.locale ([IO.Path]::GetFileName($_.Key)))) } }
            if ($library.runtimeTargets) { $library.runtimeTargets.Keys | ForEach-Object { $required.Add($_) } }
        }
    }
    # Every license text that THIRD-PARTY-NOTICES.md names ships both in the plugin folder and beside the gateway.
    $noticesText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md')
    $notices = @([regex]::Matches($noticesText, '(?<=`)licenses/[^`*]*[^`*/](?=`)') | ForEach-Object Value | Sort-Object -Unique)
    if ($notices.Count -eq 0) { throw 'THIRD-PARTY-NOTICES.md names no license text under licenses/.' }
    $notices = @('LICENSE', 'THIRD-PARTY-NOTICES.md') + $notices
    $required.AddRange([string[]] (@('CheatEngine.Mcp.Plugin.runtimeconfig.json', 'cheatengine-sdk-lua-bridge.dll', 'appsettings.json', 'README.md') + $notices))
    $missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $pluginDestination $_) -PathType Leaf) } | ForEach-Object { "CheatEngine.Mcp/$_" })
    $missing += @((@('CheatEngine.Mcp.Gateway.exe') + $notices) | Where-Object { -not (Test-Path -LiteralPath (Join-Path $dist $_) -PathType Leaf) })
    if (-not (Test-Path -LiteralPath (Join-Path $skillDestination 'SKILL.md') -PathType Leaf)) { $missing += 'skills/cheatengine-mcp/SKILL.md' }
    if ($missing.Count -ne 0) { throw "Incomplete distribution in ${dist}; missing: $($missing -join ', ')" }
    $symbols = @(Get-ChildItem -LiteralPath $pluginDestination -File -Recurse -Filter '*.pdb')
    if ($symbols.Count -ne 0) { throw "The plugin folder must not ship .pdb files (symbols are embedded): $($symbols.Name -join ', ')" }
    if (Get-ChildItem -LiteralPath $dist -File -Recurse -Filter 'local-cheat-engine.md') { throw 'references/local-cheat-engine.md must never be distributed.' }
    Get-ChildItem -LiteralPath $dist | Select-Object Name, Length
}
finally { Pop-Location }
