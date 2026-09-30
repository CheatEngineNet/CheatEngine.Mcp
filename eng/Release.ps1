#requires -Version 7.0
<#
.SYNOPSIS
Builds and packages one complete Windows x64 ZIP and SHA256SUMS.txt.
.DESCRIPTION
Without -Upload this only prepares local assets. -Upload creates a draft release or repairs an existing release.
-Publish publishes the verified draft. Git tags must already exist and match the binaries' source commit.
Existing binary assets are immutable; a changed binary needs a new version. Only the checksum file is replaced.
.EXAMPLE
pwsh -NoProfile -File eng/Release.ps1
.EXAMPLE
pwsh -NoProfile -File eng/Release.ps1 -DistributionPath artifacts/dist/release -Upload -Publish
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $DistributionPath,
    [string] $OutputDirectory,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string] $Repository = 'CheatEngineNet/CheatEngine.Mcp',
    [string] $NotesFile,
    [switch] $Upload,
    [switch] $Publish
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-GitHub([string[]] $Arguments) {
    $result = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub CLI failed: gh $($Arguments -join ' ')" }
    return $result
}

function Get-Release {
    # A draft is not returned by GitHub's get-release-by-tag endpoint.
    $pages = (Invoke-GitHub @('api', '--paginate', '--slurp', "repos/$Repository/releases?per_page=100")) |
        ConvertFrom-Json
    $matches = @($pages | ForEach-Object { $_ } | Where-Object { $_.tag_name -eq $tag })
    if ($matches.Count -gt 1) { throw "Multiple releases use $tag." }
    if ($matches.Count -eq 1) { return $matches[0] }
    return $null
}

function Test-Archive([string] $Path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = @($archive.Entries | Where-Object { $_.Name.Length -gt 0 })
        if ($entries.Count -ne $inventory.Count) { throw 'ZIP does not contain the complete distribution.' }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $entries) {
            if (-not $inventory.ContainsKey($entry.FullName) -or -not $seen.Add($entry.FullName)) {
                throw "Unexpected or duplicate ZIP entry: $($entry.FullName)"
            }
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            if ($hash -ne $inventory[$entry.FullName].Hash) { throw "ZIP hash mismatch: $($entry.FullName)" }
        }
    }
    finally { $archive.Dispose() }
}

function Assert-UploadedAsset($Asset, [string] $Path) {
    $hash = 'sha256:' + (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Asset.state -ne 'uploaded' -or $Asset.size -ne (Get-Item -LiteralPath $Path).Length -or
        $Asset.digest -ne $hash) { throw "GitHub asset does not match the local file: $($Asset.name)" }
}

function Assert-CosturaResourceInventory([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            $resources = @($metadata.ManifestResources | ForEach-Object {
                $metadata.GetString($metadata.GetManifestResource($_).Name)
            })
            $required = @(
                'CheatEngine.Client.Abstractions', 'CheatEngine.Client.Core', 'CheatEngine.Client.Extensions.DependencyInjection',
                'CheatEngine.Client.Fluent', 'CheatEngine.Client.Hosting',
                'CheatEngine.SDK', 'CheatEngine.SDK.Abi', 'CheatEngine.SDK.Annotations', 'CheatEngine.SDK.Engine',
                'CheatEngine.SDK.Hosting', 'CheatEngine.SDK.Lua', 'CheatEngine.SDK.Lua.Interop',
                'CheatEngine.Mcp.Core', 'CheatEngine.Mcp.Hosting', 'CheatEngine.Mcp.Tools',
                'CheatEngine.Mcp.Resources', 'CheatEngine.Mcp.Prompts',
                'ModelContextProtocol', 'ModelContextProtocol.Core', 'ModelContextProtocol.AspNetCore',
                'Microsoft.Extensions.AI.Abstractions'
            ) | ForEach-Object { 'costura.' + $_.ToLowerInvariant() + '.dll.compressed' }
            $required += 'costura_win_x64.cheatengine-sdk-lua-bridge.dll.compressed'
            $missing = @($required | Where-Object { $_ -cnotin $resources })
            if ($missing.Count) {
                throw "Missing embedded plugin dependencies: $($missing -join ', ')"
            }
        }
        finally { $pe.Dispose() }
    }
    finally { $stream.Dispose() }
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repoRoot
try {
    if (-not $PSCmdlet.ShouldProcess($repoRoot, 'Prepare release assets and perform the requested GitHub operations')) {
        return
    }
    if (-not $DistributionPath) {
        if ($Upload -or $Publish) {
            $status = @(git status --porcelain)
            if ($LASTEXITCODE -ne 0) { throw 'git status failed; cannot confirm a clean checkout.' }
            if ($status.Count) {
                throw 'Building a GitHub release requires a clean checkout; commit or preserve local changes first.'
            }
        }
        & (Join-Path $PSScriptRoot 'Publish.ps1') -Configuration Release
        $DistributionPath = Join-Path $repoRoot 'artifacts/dist/release'
    }
    $dist = (Resolve-Path -LiteralPath $DistributionPath).Path
    $dll = Join-Path $dist 'CheatEngine.Mcp.dll'
    $gateway = Join-Path $dist 'CheatEngine.Mcp.Gateway.exe'
    $expectedRoot = @('CheatEngine.Mcp.dll', 'CheatEngine.Mcp.Gateway.exe', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')
    $unexpected = @(Get-ChildItem -LiteralPath $dist -Force | Where-Object { $_.PSIsContainer -or $_.Name -notin $expectedRoot })
    if ($unexpected.Count) { throw "Unexpected distribution entries: $($unexpected.Name -join ', ')" }
    foreach ($path in @($dll, $gateway, (Join-Path $dist 'README.md'), (Join-Path $dist 'LICENSE'), (Join-Path $dist 'THIRD-PARTY-NOTICES.md'))) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Incomplete distribution: $path" }
    }
    $identity = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
    if ($identity -notmatch '^(?<version>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)\+(?<commit>[0-9a-f]{40})$') {
        throw 'The plugin must carry its release version and full source commit.'
    }
    $version = $Matches.version
    $sourceCommit = $Matches.commit
    $tag = "v$version"
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($gateway).ProductVersion -ne $identity) {
        throw 'Plugin and gateway were not built from the same version and commit.'
    }
    Assert-CosturaResourceInventory $dll
    $inventory = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $allEntries = @(Get-ChildItem -LiteralPath $dist -Force)
    if (@($allEntries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
        throw 'The distribution must not contain symbolic links or junctions.'
    }
    foreach ($file in @($allEntries | Where-Object { -not $_.PSIsContainer })) {
        $relative = [IO.Path]::GetRelativePath($dist, $file.FullName).Replace('\', '/')
        $inventory.Add($relative, @{ Path = $file.FullName; Hash = (Get-FileHash -LiteralPath $file.FullName).Hash })
    }
    if ($Upload -or $Publish) {
        $ref = (Invoke-GitHub @('api', "repos/$Repository/git/ref/tags/$tag")) | ConvertFrom-Json
        for ($depth = 0; $ref.object.type -eq 'tag' -and $depth -lt 4; $depth++) {
            $ref = (Invoke-GitHub @('api', "repos/$Repository/git/tags/$($ref.object.sha)")) | ConvertFrom-Json
        }
        if ($ref.object.type -ne 'commit' -or $ref.object.sha -ne $sourceCommit) {
            throw 'The remote tag does not point to the binaries source commit; tags are never created or moved here.'
        }
    }
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot "artifacts/releases/$version" }
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    $distPrefix = $dist.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($output -eq $dist -or $output.StartsWith($distPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release output must be outside the distribution.'
    }
    [IO.Directory]::CreateDirectory($output) | Out-Null
    if ((Get-Item -LiteralPath $output).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Release output must not be a symbolic link or junction.'
    }
    $zipName = "CheatEngine.Mcp-$version-win-x64.zip"
    $zip = Join-Path $output $zipName
    $checksums = Join-Path $output 'SHA256SUMS.txt'
    $obsolete = @("CheatEngine.Mcp-plugin-$version-win-x64.zip", "CheatEngine.Mcp-skill-$version.zip",
        'CheatEngine.Mcp.Gateway.exe', 'CheatEngine.Mcp.Plugin.dll', 'CheatEngine.Mcp.dll')
    $unexpected = @(Get-ChildItem -LiteralPath $output -Force | Where-Object {
        $_.PSIsContainer -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        $_.Name -notin (@($zipName, 'SHA256SUMS.txt') + $obsolete)
    })
    if ($unexpected.Count) { throw "Unexpected release-output entries: $($unexpected.Name -join ', ')" }
    $reuse = $false
    if (Test-Path -LiteralPath $zip) {
        try { Test-Archive $zip; $reuse = $true }
        catch { Write-Host 'Existing ZIP differs; preparing a verified replacement.' }
    }
    if (-not $reuse) {
        $temporary = Join-Path $output "$zipName.$([guid]::NewGuid().ToString('N')).tmp"
        try {
            $archive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
            try {
                foreach ($relative in @($inventory.Keys | Sort-Object -CaseSensitive)) {
                    $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
                    # Stable ZIP metadata: rebuilding the same contents does not change container hashes.
                    $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                    $source = [IO.File]::OpenRead($inventory[$relative].Path)
                    $destination = $entry.Open()
                    try { $source.CopyTo($destination) }
                    finally { $source.Dispose(); $destination.Dispose() }
                }
            }
            finally { $archive.Dispose() }
            Test-Archive $temporary
            [IO.File]::Move($temporary, $zip, $true)
        }
        finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
    }
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksums, "$hash  $zipName`n", [Text.UTF8Encoding]::new($false))
    foreach ($name in $obsolete) {
        $path = Join-Path $output $name
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
    }
    Write-Host "Verified $($inventory.Count) flat files, including CheatEngine.Mcp.dll."
    Get-ChildItem -LiteralPath $output | Select-Object Name, Length
    if (-not ($Upload -or $Publish)) { return }

    $release = Get-Release
    if ($null -eq $release) {
        $arguments = @('release', 'create', $tag, '--repo', $Repository, '--verify-tag', '--draft',
            '--latest=false', '--title', "CheatEngine.Mcp $version")
        if ($version.Contains('-')) { $arguments += '--prerelease' }
        if ($NotesFile) { $arguments += @('--notes-file', (Resolve-Path -LiteralPath $NotesFile).Path) }
        else {
            $notes = "Download $zipName and extract the complete folder. Install CheatEngine.Mcp.dll as the single plugin DLL. " +
                'Configure your AI client to start CheatEngine.Mcp.Gateway.exe as a stdio MCP server. ' +
                "See the included README for installed .NET prerequisites. Verify the ZIP using SHA256SUMS.txt.`n`n" +
                "Built from $sourceCommit. Operator guidance ships as MCP resources and prompts; no separate skill is required."
            $arguments += @('--notes', $notes)
        }
        Invoke-GitHub $arguments | Out-Host
        $release = Get-Release
    }
    # Refuse changed binaries before any upload or deletion. Existing releases retain their validated payload.
    $existing = @($release.assets | Where-Object { $_.name -eq $zipName })
    if ($existing.Count) { Assert-UploadedAsset $existing[0] $zip }
    $foreign = @($release.assets | Where-Object { $_.name -notin (@($zipName, 'SHA256SUMS.txt') + $obsolete) })
    if ($foreign.Count) { throw 'Release contains unrecognized assets; preserve them and review the layout manually.' }
    foreach ($path in @($zip, $checksums)) {
        $name = [IO.Path]::GetFileName($path)
        $asset = @($release.assets | Where-Object { $_.name -eq $name })
        $matches = $false
        if ($asset.Count) {
            try { Assert-UploadedAsset $asset[0] $path; $matches = $true }
            catch { if ($name -ne 'SHA256SUMS.txt') { throw } }
        }
        if (-not $matches) {
            $arguments = @('release', 'upload', $tag, $path, '--repo', $Repository)
            if ($asset.Count) { $arguments += '--clobber' } # Only a checksum replacement reaches this path.
            Invoke-GitHub $arguments | Out-Host
        }
    }
    $release = Get-Release
    foreach ($path in @($zip, $checksums)) {
        $name = [IO.Path]::GetFileName($path)
        $asset = @($release.assets | Where-Object { $_.name -eq $name })
        if ($asset.Count -ne 1) { throw "Missing or duplicate GitHub asset: $name" }
        Assert-UploadedAsset $asset[0] $path
    }
    $zipAsset = @($release.assets | Where-Object { $_.name -eq $zipName })[0]
    Invoke-GitHub @('api', '--method', 'PATCH', "repos/$Repository/releases/assets/$($zipAsset.id)",
        '-f', 'label=Windows x64: single plugin DLL, gateway and instructions') | Out-Null
    # Delete only the named obsolete assets, after both standard assets have been verified on GitHub.
    foreach ($asset in @($release.assets | Where-Object { $_.name -in $obsolete })) {
        Invoke-GitHub @('api', '--method', 'DELETE', "repos/$Repository/releases/assets/$($asset.id)") | Out-Null
    }
    if ($NotesFile -and $null -ne $release) {
        Invoke-GitHub @('release', 'edit', $tag, '--repo', $Repository, '--notes-file',
            (Resolve-Path -LiteralPath $NotesFile).Path) | Out-Host
    }
    if ($Publish) {
        $prerelease = $version.Contains('-').ToString().ToLowerInvariant()
        $latest = (-not $version.Contains('-')).ToString().ToLowerInvariant()
        Invoke-GitHub @('release', 'edit', $tag, '--repo', $Repository, '--draft=false',
            "--prerelease=$prerelease", "--latest=$latest") | Out-Host
    }
    $release = Get-Release
    if ($release.assets.Count -ne 2) { throw 'Final release does not have exactly the ZIP and checksum assets.' }
    Write-Host "Verified release: $($release.html_url) (draft=$($release.draft))"
}
finally { Pop-Location }
