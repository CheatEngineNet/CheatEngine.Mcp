#requires -Version 7.0
<#
.SYNOPSIS
Verifies and extracts a release ZIP into a new qualification-only directory.
.DESCRIPTION
No binaries are executed and no existing deployment is replaced. A failed extraction is retained for inspection.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ZipPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string] $ExpectedSha256,
    [Parameter(Mandatory)][string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-NoReparseAncestor([string] $Path) {
    for ($current = [IO.Path]::GetFullPath($Path); $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Qualification package paths must not traverse a symbolic link or junction.'
        }
    }
}

function Get-BoundedEntryHash([IO.Stream] $InputStream, [long] $ExpectedLength, [long] $MaximumLength) {
    $hasher = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $buffer = [byte[]]::new(65536)
        [long] $length = 0
        while (($read = $InputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $length += $read
            if ($length -gt $ExpectedLength -or $length -gt $MaximumLength) { throw 'An expanded entry exceeds its declared size.' }
            $hasher.AppendData($buffer, 0, $read)
        }
        if ($length -ne $ExpectedLength) { throw 'An expanded entry does not match its declared size.' }
        return [Convert]::ToHexString($hasher.GetHashAndReset())
    }
    finally { $hasher.Dispose() }
}

$source = [IO.Path]::GetFullPath($ZipPath)
$destination = [IO.Path]::GetFullPath($OutputDirectory)
Assert-NoReparseAncestor $source
Assert-NoReparseAncestor $destination
if (Test-Path -LiteralPath $destination) { throw 'The qualification destination must not already exist.' }
$expected = @('CheatEngine.Mcp.dll', 'CheatEngine.Mcp.Gateway.exe', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')
$maximumBytes = 128MB
$stream = [IO.File]::Open($source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    if ($stream.Length -gt $maximumBytes) { throw 'The ZIP exceeds the qualification size bound.' }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
    if ($hash -cne $ExpectedSha256.ToUpperInvariant()) { throw 'The ZIP SHA-256 does not match the reviewed release.' }
    $stream.Position = 0
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
    try {
        if ($archive.Entries.Count -ne $expected.Count) { throw 'The ZIP must contain exactly the five flat release files.' }
        $inventory = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        [long] $totalBytes = 0
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -cnotin $expected -or $inventory.ContainsKey($entry.FullName)) {
                throw 'The ZIP contains an unexpected or duplicate entry.'
            }
            $totalBytes += $entry.Length
            if ($entry.Length -le 0 -or $totalBytes -gt $maximumBytes) { throw 'The expanded ZIP exceeds its non-empty file bounds.' }
            $input = $entry.Open()
            try { $entryHash = Get-BoundedEntryHash $input $entry.Length $maximumBytes }
            finally { $input.Dispose() }
            $inventory.Add($entry.FullName, @{ Length = $entry.Length; Sha256 = $entryHash })
        }

        # Complete hash and inventory validation precedes the first destination write.
        [IO.Directory]::CreateDirectory($destination) | Out-Null
        foreach ($entry in $archive.Entries) {
            $path = Join-Path $destination $entry.FullName
            $input = $entry.Open()
            try {
                $output = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $input.CopyTo($output) }
                finally { $output.Dispose() }
            }
            finally { $input.Dispose() }
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $inventory[$entry.FullName].Sha256) {
                throw 'An extracted file did not retain its archive hash.'
            }
        }
        $pluginVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $destination 'CheatEngine.Mcp.dll')).ProductVersion
        $gatewayVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $destination 'CheatEngine.Mcp.Gateway.exe')).ProductVersion
        if ($pluginVersion -cnotmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\+[0-9a-f]{40}$' -or
            $pluginVersion -cne $gatewayVersion) { throw 'The extracted plugin and gateway source identities do not agree.' }
        [pscustomobject]@{ ZipSha256 = $hash; ProductVersion = $pluginVersion; Directory = $destination; Files = $inventory }
    }
    finally { $archive.Dispose() }
}
finally { $stream.Dispose() }
