$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "CheatEngine.Mcp.BundleWriter/$([Guid]::NewGuid().ToString('N'))"
$writer = Join-Path $PSScriptRoot 'Write-Bundle.ps1'
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $source = Join-Path $testRoot 'source.txt'
    $manifest = Join-Path $testRoot 'manifest.txt'
    $output = Join-Path $testRoot 'payload.zip'
    [IO.File]::WriteAllText($source, 'complete payload')
    [IO.File]::WriteAllText($manifest, "$source|nested/file.txt")
    & $writer -Manifest $manifest -OutputPath $output
    $firstHash = (Get-FileHash -LiteralPath $output).Hash
    & $writer -Manifest $manifest -OutputPath $output
    if ((Get-FileHash -LiteralPath $output).Hash -ne $firstHash) { throw 'Bundle output is not deterministic.' }

    [IO.File]::WriteAllText($manifest, '')
    $rejected = $false
    try { & $writer -Manifest $manifest -OutputPath $output }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Writer accepted an empty payload.' }
    if ((Get-FileHash -LiteralPath $output).Hash -ne $firstHash) { throw 'Empty manifest replaced the last complete output.' }

    foreach ($invalid in @('../outside.txt', '/absolute.txt', 'file:stream', 'sub//file', 'sub/./file', 'sub./file', 'sub /file')) {
        [IO.File]::WriteAllText($manifest, "$source|$invalid")
        $rejected = $false
        try { & $writer -Manifest $manifest -OutputPath $output }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Writer accepted invalid path: $invalid" }
        if ((Get-FileHash -LiteralPath $output).Hash -ne $firstHash) { throw 'A failed bundle build replaced the last complete output.' }
    }

    [IO.File]::WriteAllText($manifest, "$source|nested/file.txt")
    [IO.File]::WriteAllText($output, 'old corrupt output')
    & $writer -Manifest $manifest -OutputPath $output
    if ((Get-FileHash -LiteralPath $output).Hash -ne $firstHash) { throw 'Regeneration did not replace the corrupt output.' }
    if (@(Get-ChildItem -LiteralPath $testRoot -Filter '*.tmp').Count -ne 0) { throw 'Bundle writer left staging files.' }
    Write-Output 'Bundle writer: deterministic output, path rejection, failure preservation, and replacement passed.'
}
finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $expectedParent = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'CheatEngine.Mcp.BundleWriter')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTestRoot.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside the bundle test directory.' }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
