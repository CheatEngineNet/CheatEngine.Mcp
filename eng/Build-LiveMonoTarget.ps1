param(
	[string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\live-mono-target')
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $repository 'eng\LiveMonoTarget\Program.cs'
$runtimeRoot = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts\fixtures\mono-6.12.0.206-x64\extracted\runtime'))
$mono = Join-Path $runtimeRoot 'bin\mono.exe'
$monoNativeLibrary = Join-Path $runtimeRoot 'bin\mono-2.0-sgen.dll'
$runtimeConfig = Join-Path $runtimeRoot 'etc\mono\config'
$mscorlib = Join-Path $runtimeRoot 'lib\mono\4.5\mscorlib.dll'
$approvedOutput = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts\live-mono-target'))
$requestedOutput = [IO.Path]::GetFullPath($OutputDirectory)

function Assert-NoReparsePointAncestor {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Path,
		[Parameter(Mandatory = $true)]
		[string]$Description
	)

	for ($cursor = [IO.Path]::GetFullPath($Path); $cursor;) {
		if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
			throw "Mono fixture $Description refuses reparse-point path: $cursor"
		}

		$parent = Split-Path $cursor -Parent
		if ([string]::IsNullOrEmpty($parent) -or $parent -eq $cursor) {
			break
		}
		$cursor = $parent
	}
}

function Get-ExpectedSha256 {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Path,
		[Parameter(Mandatory = $true)]
		[string]$ExpectedHash,
		[Parameter(Mandatory = $true)]
		[string]$Description
	)

	$actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
	if (![string]::Equals($actualHash, $ExpectedHash, [StringComparison]::OrdinalIgnoreCase)) {
		throw "Unexpected SHA256 for ${Description}: $actualHash"
	}
	return $actualHash
}

if (!$requestedOutput.StartsWith($approvedOutput + [IO.Path]::DirectorySeparatorChar,
	[StringComparison]::OrdinalIgnoreCase) -and $requestedOutput -ne $approvedOutput) {
	throw "The fixed Mono fixture output must remain under $approvedOutput."
}
if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing fixed Mono fixture source: $source" }
if (!(Test-Path -LiteralPath $mono -PathType Leaf)) { throw "Missing approved private Mono runtime executable: $mono" }
if (!(Test-Path -LiteralPath $monoNativeLibrary -PathType Leaf)) { throw "Missing approved private Mono native library: $monoNativeLibrary" }
if (!(Test-Path -LiteralPath $runtimeConfig -PathType Leaf)) { throw "Missing approved private Mono configuration: $runtimeConfig" }
if (!(Test-Path -LiteralPath $mscorlib -PathType Leaf)) { throw "Missing approved private Mono class library: $mscorlib" }
$csc = Join-Path ${env:WINDIR} 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $csc -PathType Leaf)) { throw "Required .NET Framework x64 C# compiler was not found: $csc" }

foreach ($pathSpec in @(
		@($source, 'source'),
		@($runtimeRoot, 'runtime root'),
		@($mono, 'Mono executable'),
		@($monoNativeLibrary, 'Mono native library'),
		@($runtimeConfig, 'Mono configuration'),
		@($mscorlib, 'Mono class library'),
		@($csc, 'Framework compiler'),
		@($approvedOutput, 'approved output'),
		@($requestedOutput, 'requested output')
	)) {
	Assert-NoReparsePointAncestor -Path $pathSpec[0] -Description $pathSpec[1]
}

$monoHash = Get-ExpectedSha256 -Path $mono -ExpectedHash '70AFA7611A4913DEB03C84F9EE0C1E24CED8EE0558EA8C4D60317637417BF0EC' -Description 'mono.exe'
$monoNativeLibraryHash = Get-ExpectedSha256 -Path $monoNativeLibrary -ExpectedHash '07A1DEBE6CED8E31FB944046EB6531D41969BAE1F686CF49A13F06877D77D55D' -Description 'mono-2.0-sgen.dll'
$mscorlibHash = Get-ExpectedSha256 -Path $mscorlib -ExpectedHash 'C7512C133808ACB42E48B6C1B879AB6F3C988A9E7A83B6A80E75A9FF38B6E5CF' -Description 'mscorlib.dll'
$runtimeConfigHash = Get-ExpectedSha256 -Path $runtimeConfig -ExpectedHash 'B6B3E96E9D8EFA31EC6B671F5EA0CBF2D058152BC782B3C53865EBCFBBBCCB1E' -Description 'etc/mono/config'

$outputExe = Join-Path $requestedOutput 'CheatEngine.Mcp.LiveMonoTarget.exe'
$identityPath = Join-Path $requestedOutput 'fixture-identity.json'
foreach ($leaf in @($outputExe, $identityPath)) {
	if (Test-Path -LiteralPath $leaf) { throw "Mono fixture build requires unused output files: $leaf" }
}
New-Item -ItemType Directory -Force -Path $requestedOutput | Out-Null
& $csc /nologo /target:exe /platform:x64 /out:$outputExe $source
if ($LASTEXITCODE -ne 0) { throw 'The fixed Mono target did not compile.' }

$identity = [ordered]@{
	architecture = 'x64'
	cscPath = $csc
	cscSha256 = (Get-FileHash -LiteralPath $csc -Algorithm SHA256).Hash
	runtimeRoot = $runtimeRoot
	monoPath = $mono
	monoSha256 = $monoHash
	monoNativeLibraryPath = $monoNativeLibrary
	monoNativeLibrarySha256 = $monoNativeLibraryHash
	runtimeConfigPath = $runtimeConfig
	runtimeConfigSha256 = $runtimeConfigHash
	mscorlibPath = $mscorlib
	mscorlibSha256 = $mscorlibHash
	runtimeProvenance = 'Approved private official Mono 6.12.0.206 x64 extraction; mscorlib IL and object.h inspected before fixture promotion.'
	targetPath = $outputExe
	targetSha256 = (Get-FileHash -LiteralPath $outputExe -Algorithm SHA256).Hash
	sourcePath = $source
	sourceSha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
}
$identity | ConvertTo-Json | Set-Content -LiteralPath $identityPath -Encoding utf8
