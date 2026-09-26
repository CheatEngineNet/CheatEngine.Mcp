param(
    [Parameter(Mandatory)][string] $Manifest,
    [Parameter(Mandatory)][string] $OutputPath
)
$ErrorActionPreference = 'Stop'
$entries = Get-Content -LiteralPath $Manifest | Sort-Object
if (@($entries).Count -eq 0) { throw 'The plugin payload manifest is empty.' }
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$output = [IO.MemoryStream]::new()
$zip = [IO.Compression.ZipArchive]::new($output, [IO.Compression.ZipArchiveMode]::Create, $true)
try {
    foreach ($line in $entries) {
        $source, $relative = $line.Split('|', 2)
        $relative = $relative.Replace('\', '/')
        $invalidParts = @($relative.Split('/') | Where-Object {
            $_ -in @('', '.', '..') -or $_.EndsWith(' ') -or $_.EndsWith('.')
        })
        if (-not $relative -or $relative.StartsWith('/') -or $relative.Contains(':') -or
            $invalidParts.Count -ne 0 -or -not $names.Add($relative)) {
            throw "Invalid or duplicate payload path: $relative"
        }
        $entry = $zip.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $input = [IO.File]::OpenRead($source)
        $destination = $entry.Open()
        try { $input.CopyTo($destination) }
        finally { $destination.Dispose(); $input.Dispose() }
    }
}
finally { $zip.Dispose() }
try {
    $bytes = $output.ToArray()
    $outputFullPath = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputFullPath)) | Out-Null
    $stagingPath = "$outputFullPath.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $staging = [IO.FileStream]::new($stagingPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $staging.Write($bytes); $staging.Flush($true) }
        finally { $staging.Dispose() }
        [IO.File]::Move($stagingPath, $outputFullPath, $true)
    }
    finally { if ([IO.File]::Exists($stagingPath)) { [IO.File]::Delete($stagingPath) } }
}
finally { $output.Dispose() }
