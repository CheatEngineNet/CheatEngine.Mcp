param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [string] $OutputDirectory
)
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
    $dist = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot "artifacts/dist/$variant" }
    $allowedDistRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/dist')) + [IO.Path]::DirectorySeparatorChar
    if (-not $dist.StartsWith($allowedDistRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Distribution output must be a subdirectory of artifacts/dist.'
    }
    [IO.Directory]::CreateDirectory($dist) | Out-Null
    if ((Get-Item -LiteralPath $dist).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Distribution output must not be a symbolic link or junction.'
    }
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
    # Remove only known obsolete layouts. Operator guidance is served through MCP resources and prompts.
    foreach ($stale in @('CheatEngine.Mcp', 'skills', 'licenses')) { Remove-Contained (Join-Path $dist $stale) $distRoot | Out-Null }
    $expected = @('CheatEngine.Mcp.dll', 'CheatEngine.Mcp.Gateway.exe', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')
    $unexpected = @(Get-ChildItem -LiteralPath $dist -Force | Where-Object { $_.Name -notin $expected -or $_.PSIsContainer -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) })
    if ($unexpected.Count -ne 0) { throw "Unexpected files in distribution folder $dist. Move them before publishing." }

    # Use standard framework-dependent dotnet publish. Costura embeds copy-local managed dependencies and the SDK's
    # native bridge; only the resulting DLL is installed. Build-only component manifests stay in artifacts/publish.
    # CheatEnginePluginOutputPath selects Client's folder deployment, so it is deliberately not used for this bundle.
    Remove-Contained (Join-Path $repoRoot "artifacts/bin/CheatEngine.Mcp.Plugin/$variant") $binRoot | Out-Null
    $publishRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/publish')) + [IO.Path]::DirectorySeparatorChar
    $pluginOutput = Remove-Contained (Join-Path $repoRoot "artifacts/publish/CheatEngine.Mcp.Plugin/$variant") $publishRoot
    dotnet publish srcs/CheatEngine.Mcp.Plugin -c $Configuration -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true -o $pluginOutput
    if ($LASTEXITCODE -ne 0) { throw 'Bundled plugin publish failed.' }
    Copy-Item -LiteralPath (Join-Path $pluginOutput 'CheatEngine.Mcp.dll') -Destination (Join-Path $dist 'CheatEngine.Mcp.dll') -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'srcs/CheatEngine.Mcp.Plugin/Distribution/README.md') -Destination (Join-Path $dist 'README.md') -Force
    # Probe the exact installed DLL from a fresh process, with no adjacent dependency or runtime configuration files.
    # The runtimeconfig below belongs to the probe host and remains a build output, never a distributed sidecar.
    dotnet build tests/CheatEngine.Mcp.LiveTarget -c $Configuration -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Plugin bundle probe build failed.' }
    $probe = Join-Path $repoRoot "artifacts/bin/CheatEngine.Mcp.LiveTarget/$variant/CheatEngine.Mcp.LiveTarget.dll"
    dotnet exec --runtimeconfig (Join-Path $pluginOutput 'CheatEngine.Mcp.runtimeconfig.json') $probe --probe-plugin (Join-Path $dist 'CheatEngine.Mcp.dll')
    if ($LASTEXITCODE -ne 0) { throw 'The published single-DLL plugin failed the isolated load probe.' }

    dotnet publish srcs/CheatEngine.Mcp.Gateway -c $Configuration -p:PublishProfile=Standalone -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Native AOT gateway publish failed.' }
    $gateway = Join-Path $dist 'CheatEngine.Mcp.Gateway.exe'
    Copy-Item -LiteralPath (Join-Path $repoRoot "artifacts/publish/CheatEngine.Mcp.Gateway/$variant-nativeaot/CheatEngine.Mcp.Gateway.exe") -Destination $gateway -Force
    Invoke-GatewayNativeAotSmoke $gateway
    # The native gateway has no managed runtime beside it and shares the packages' notices with the plugin.
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'), (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $dist -Force

    # THIRD-PARTY-NOTICES.md reproduces the redistributed license texts. The release packager also verifies the
    # embedded dependency inventory without executing the plugin, then checks every ZIP entry and its checksum.
    $noticesText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md')
    foreach ($heading in @('## MIT License', '## Apache License 2.0')) {
        if (-not $noticesText.Contains($heading)) { throw "THIRD-PARTY-NOTICES.md lacks the '$heading' section." }
    }
    $missing = @($expected | Where-Object { -not (Test-Path -LiteralPath (Join-Path $dist $_) -PathType Leaf) })
    if ($missing.Count -ne 0) { throw "Incomplete distribution in ${dist}; missing: $($missing -join ', ')" }
    $files = @(Get-ChildItem -LiteralPath $dist -Force)
    if ($files.Count -ne $expected.Count) { throw 'The distribution must contain exactly the single plugin DLL, gateway, instructions and notices.' }
    Get-ChildItem -LiteralPath $dist | Select-Object Name, Length
}
finally { Pop-Location }
