[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[ValidateScript({ [IO.Path]::IsPathFullyQualified($_) })]
	[string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'LiveNetworkProbe\CemcpNetworkProbe.cpp'))
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing fixture source: $source" }

$vswhere = @(
	(Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)) 'Microsoft Visual Studio\Installer\vswhere.exe'),
	(Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'Microsoft Visual Studio\Installer\vswhere.exe')
) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ($null -eq $vswhere) { throw 'vswhere.exe was not found; install or select an existing MSVC C++ toolchain.' }

function Invoke-BoundedProcess([string] $FileName, [string[]] $Arguments, [int] $TimeoutMilliseconds, [string] $RawArguments = '') {
	$process = [Diagnostics.Process]::new()
	$process.StartInfo = [Diagnostics.ProcessStartInfo]::new()
	$process.StartInfo.FileName = $FileName
	$process.StartInfo.UseShellExecute = $false
	$process.StartInfo.CreateNoWindow = $true
	$process.StartInfo.RedirectStandardOutput = $true
	$process.StartInfo.RedirectStandardError = $true
	if ($RawArguments) {
		$process.StartInfo.Arguments = $RawArguments
	} else {
		foreach ($argument in $Arguments) { [void]$process.StartInfo.ArgumentList.Add($argument) }
	}
	if (-not $process.Start()) { throw "Process did not start: $FileName" }
	$stdoutTask = $process.StandardOutput.ReadToEndAsync()
	$stderrTask = $process.StandardError.ReadToEndAsync()
	if (-not $process.WaitForExit($TimeoutMilliseconds)) {
		$process.Kill($true)
		if (-not $process.WaitForExit(10000)) { throw "Process did not stop after its deadline: $FileName" }
		throw "Process exceeded its $TimeoutMilliseconds millisecond deadline: $FileName"
	}
	$stdout = $stdoutTask.GetAwaiter().GetResult()
	$stderr = $stderrTask.GetAwaiter().GetResult()
	if ($process.ExitCode -ne 0) { throw "Process failed with exit code $($process.ExitCode): $FileName`n$stderr" }
	return $stdout.Trim()
}

$installation = Invoke-BoundedProcess $vswhere @('-latest', '-products', '*', '-requires', 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-property', 'installationPath') 10000
if ([string]::IsNullOrWhiteSpace($installation)) { throw 'No Visual Studio installation with x64 MSVC tools was found.' }
$devCmd = Join-Path $installation 'Common7\Tools\VsDevCmd.bat'
if (-not (Test-Path -LiteralPath $devCmd -PathType Leaf)) { throw "VsDevCmd.bat was not found: $devCmd" }

$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$nonce = [Guid]::NewGuid().ToString('N')
$object = Join-Path $output "CemcpNetworkProbe-$nonce.obj"
$compilerPdb = Join-Path $output "CemcpNetworkProbe-$nonce.compile.pdb"
$dll = Join-Path $output "CemcpNetworkProbe-$nonce.dll"
$pdb = Join-Path $output "CemcpNetworkProbe-$nonce.pdb"
$importLibrary = Join-Path $output "CemcpNetworkProbe-$nonce.lib"
$manifest = Join-Path $output "CemcpNetworkProbe-$nonce.manifest.json"

# No network, package restore, or third-party input. The command has one bounded source and owned output paths.
$command = 'call "' + $devCmd + '" -arch=amd64 -host_arch=amd64' +
	' && cl.exe /nologo /std:c++20 /c /EHsc /GS /W4 /Od /Zi /Fd"' + $compilerPdb + '" /favor:AMD64 /Fo"' + $object + '" "' + $source + '"' +
	' && link.exe /nologo /DLL /DEBUG /INCREMENTAL:NO /MACHINE:X64 /OUT:"' + $dll + '" /PDB:"' + $pdb + '" /IMPLIB:"' + $importLibrary + '" "' + $object + '" ws2_32.lib'
# cmd.exe parses quotes itself; ArgumentList's C-runtime escaping would turn them into literal backslashes.
[void](Invoke-BoundedProcess $env:ComSpec @() 60000 ('/d /s /c "' + $command + '"'))
if (-not (Test-Path -LiteralPath $dll -PathType Leaf) -or -not (Test-Path -LiteralPath $pdb -PathType Leaf)) {
	throw 'MSVC did not produce both the owned DLL and matching PDB.'
}

$facts = [ordered]@{
    fixture = 'CemcpNetworkProbe'
    architecture = 'x64'
    source = [ordered]@{ path = $source; sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash }
    dll = [ordered]@{ path = $dll; sha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash; length = (Get-Item -LiteralPath $dll).Length }
    pdb = [ordered]@{ path = $pdb; sha256 = (Get-FileHash -LiteralPath $pdb -Algorithm SHA256).Hash; length = (Get-Item -LiteralPath $pdb).Length }
    exports = @('luaopen_cemcpnetworkprobe')
    functions = @('ipv4', 'ipv6', 'pid')
    destinations = @('192.0.2.1', '2001:db8::1')
    destinationPort = 9
    expectedBlockedError = 10013
    maximumPendingConnectSeconds = 1
}
$facts | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifest -Encoding utf8NoBOM
Remove-Item -LiteralPath $object, $compilerPdb, $importLibrary, ([IO.Path]::ChangeExtension($importLibrary, '.exp')) -Force -ErrorAction SilentlyContinue
$manifest
