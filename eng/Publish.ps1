param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')
$ErrorActionPreference = 'Stop'

function Invoke-GatewayNativeAotSmoke([string] $gateway) {
    $registry = Join-Path ([IO.Path]::GetTempPath()) "CheatEngine.Mcp.Gateway-smoke-$([guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($registry) | Out-Null
    $process = [Diagnostics.Process]::new()
    $started = $false
    try {
        $startInfo = [Diagnostics.ProcessStartInfo]::new($gateway)
        $startInfo.WorkingDirectory = Split-Path -Parent $gateway
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardInput = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.ArgumentList.Add('--instance-directory')
        $startInfo.ArgumentList.Add($registry)
        $process.StartInfo = $startInfo
        if (-not $process.Start()) { throw 'Gateway process did not start.' }
        $started = $true

        function Read-McpResponse([int] $id) {
            $read = $process.StandardOutput.ReadLineAsync()
            if (-not $read.Wait(15000)) { throw "Timed out waiting for MCP response $id." }
            $line = $read.GetAwaiter().GetResult()
            if ($null -eq $line) { throw "Gateway closed stdout before MCP response $id." }
            try { $message = $line | ConvertFrom-Json -ErrorAction Stop }
            catch { throw "Gateway stdout was not JSON-RPC: $line" }
            if ($message.id -ne $id) { throw "Expected MCP response $id, got: $line" }
            if ($null -ne $message.error) { throw "MCP response $id was an error: $line" }
            return $message
        }

        $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"CheatEngine.Mcp.Publish","version":"2.0.0"}}}')
        $process.StandardInput.Flush()
        $initialize = Read-McpResponse 1
        if ($initialize.result.serverInfo.name -ne 'CheatEngine.Mcp.Gateway') {
            throw "Unexpected MCP server '$($initialize.result.serverInfo.name)'."
        }

        $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
        $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}')
        $process.StandardInput.Flush()
        $tools = Read-McpResponse 2
        if (@($tools.result.tools.name) -notcontains 'instance_list') {
            throw 'Gateway did not publish its local instance_list tool.'
        }

        foreach ($request in @(
                @{ Id = 3; Method = 'resources/list' },
                @{ Id = 4; Method = 'resources/templates/list' },
                @{ Id = 5; Method = 'prompts/list' })) {
            $process.StandardInput.WriteLine("{`"jsonrpc`":`"2.0`",`"id`":$($request.Id),`"method`":`"$($request.Method)`",`"params`":{}}")
            $process.StandardInput.Flush()
            $null = Read-McpResponse $request.Id
        }

        $process.StandardInput.Close()
        if (-not $process.WaitForExit(10000)) { throw 'Gateway did not stop within 10 seconds.' }
        $stderr = $process.StandardError.ReadToEnd()
        if ($process.ExitCode -ne 0) { throw "Gateway exited with code $($process.ExitCode): $stderr" }
    }
    finally {
        if ($started -and -not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()
        if ([IO.Directory]::Exists($registry)) { [IO.Directory]::Delete($registry, $true) }
    }
}

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
    # Earlier layouts also wrote skills/ and licenses/; the knowledge now ships inside the plugin (MCP resources and
    # prompts) and THIRD-PARTY-NOTICES.md carries the license texts, so those folders are only stale output.
    foreach ($stale in @('skills', 'licenses')) { Remove-Contained (Join-Path $dist $stale) $distRoot | Out-Null }
    $folders = @('CheatEngine.Mcp')
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
    Copy-Item -LiteralPath (Join-Path $repoRoot 'srcs/CheatEngine.Mcp.Plugin/Distribution/README.md') -Destination (Join-Path $pluginDestination 'README.md')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'), (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $pluginDestination

    dotnet publish srcs/CheatEngine.Mcp.Gateway -c $Configuration -p:PublishProfile=Standalone -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Native AOT gateway publish failed.' }
    $gateway = Join-Path $dist 'CheatEngine.Mcp.Gateway.exe'
    Copy-Item -LiteralPath (Join-Path $repoRoot "artifacts/publish/CheatEngine.Mcp.Gateway/$variant-nativeaot/CheatEngine.Mcp.Gateway.exe") -Destination $gateway -Force
    Invoke-GatewayNativeAotSmoke $gateway
    # The native gateway has no managed runtime beside it and shares the packages' notices with the plugin.
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'), (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $dist -Force

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
    # THIRD-PARTY-NOTICES.md is self-contained (it reproduces the MIT and Apache-2.0 texts); it ships with LICENSE both in
    # the plugin folder and beside the gateway.
    $noticesText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md')
    foreach ($heading in @('## MIT License', '## Apache License 2.0')) {
        if (-not $noticesText.Contains($heading)) { throw "THIRD-PARTY-NOTICES.md lacks the '$heading' section." }
    }
    $notices = @('LICENSE', 'THIRD-PARTY-NOTICES.md')
    $required.AddRange([string[]] (@('CheatEngine.Mcp.Plugin.runtimeconfig.json', 'cheatengine-sdk-lua-bridge.dll', 'appsettings.json', 'README.md') + $notices))
    $missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $pluginDestination $_) -PathType Leaf) } | ForEach-Object { "CheatEngine.Mcp/$_" })
    $missing += @((@('CheatEngine.Mcp.Gateway.exe') + $notices) | Where-Object { -not (Test-Path -LiteralPath (Join-Path $dist $_) -PathType Leaf) })
    if ($missing.Count -ne 0) { throw "Incomplete distribution in ${dist}; missing: $($missing -join ', ')" }
    $symbols = @(Get-ChildItem -LiteralPath $pluginDestination -File -Recurse -Filter '*.pdb')
    if ($symbols.Count -ne 0) { throw "The plugin folder must not ship .pdb files (symbols are embedded): $($symbols.Name -join ', ')" }
    Get-ChildItem -LiteralPath $dist | Select-Object Name, Length
}
finally { Pop-Location }
