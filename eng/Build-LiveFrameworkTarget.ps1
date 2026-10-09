param(
	[ValidateSet('x86', 'x64')][string]$Architecture = 'x64',
	[string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\live-framework-target')
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $repository 'eng\LiveFrameworkTarget\Program.cs'
$approvedOutput = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts\live-framework-target'))
$requestedOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (!$requestedOutput.StartsWith($approvedOutput + [IO.Path]::DirectorySeparatorChar,
	[StringComparison]::OrdinalIgnoreCase) -and $requestedOutput -ne $approvedOutput) {
	throw "The fixed fixture output must remain under $approvedOutput."
}
$csc = Join-Path ${env:WINDIR} 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if ($Architecture -eq 'x86') { $csc = Join-Path ${env:WINDIR} 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $csc)) { throw "Required .NET Framework 4 C# compiler was not found: $csc" }
$destination = Join-Path $requestedOutput $Architecture
foreach ($ancestor in @($approvedOutput, $requestedOutput, $destination)) {
	if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
		throw "Fixture output refuses the reparse-point path: $ancestor"
	}
}
for ($cursor = $destination; $cursor; $cursor = Split-Path $cursor -Parent) {
	if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Fixture output refuses reparse ancestor: $cursor" }
}
$outputExe = Join-Path $destination 'CheatEngine.Mcp.LiveFrameworkTarget.exe'
$identityPath = Join-Path $destination 'fixture-identity.json'
foreach ($leaf in @($outputExe, $identityPath)) {
	if (Test-Path -LiteralPath $leaf) { throw "Fixture build requires unused output files: $leaf" }
}
New-Item -ItemType Directory -Force -Path $destination | Out-Null
& $csc /nologo /target:winexe "/platform:$Architecture" "/out:$outputExe" $source
if ($LASTEXITCODE -ne 0) { throw 'The fixed .NET Framework target did not compile.' }
$identity = [ordered]@{
	architecture = $Architecture
	cscPath = $csc
	cscSha256 = (Get-FileHash -LiteralPath $csc -Algorithm SHA256).Hash
	targetPath = $outputExe
	targetSha256 = (Get-FileHash -LiteralPath $outputExe -Algorithm SHA256).Hash
	sourceSha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
}
$identity | ConvertTo-Json | Set-Content -LiteralPath $identityPath -Encoding utf8
