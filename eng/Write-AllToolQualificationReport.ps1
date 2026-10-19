#requires -Version 7.0
<#
.SYNOPSIS
Writes one evidence row per advertised tool without treating routing or refusals as native qualification.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $GatewayTrx,
    [string] $PortableTrx,
    [string] $NativeLuaTrx,
    [string] $LiveSummary,
    [string] $LiveEvidencePath = (Join-Path $PSScriptRoot '../docs/qualification/all-tools/live-evidence.json'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/all-tools/report')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-TestReport([string] $Path) {
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create([IO.Path]::GetFullPath($Path), $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.Load($reader)
        $namespace = [Xml.XmlNamespaceManager]::new($document.NameTable)
        $namespace.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $results = @($document.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $namespace))
        if ($results.Count -eq 0) { throw "No test results in '$Path'." }
        $cases = @{}
        $occurrences = @{}
        foreach ($result in $results) {
            $name = $result.GetAttribute('testName')
            $prior = if ($occurrences.ContainsKey($name)) { [int] $occurrences[$name] } else { 0 }
            $occurrences[$name] = 1 + $prior
            $outcome = $result.GetAttribute('outcome')
            if (-not $cases.ContainsKey($name) -or (Outcome-Rank $outcome) -gt (Outcome-Rank $cases[$name])) {
                $cases[$name] = $outcome
            }
        }
        return @{
            Cases = $cases
            Occurrences = $occurrences
            Summary = [ordered]@{
                path = [IO.Path]::GetFullPath($Path)
                sha256 = (Get-FileHash -LiteralPath $Path).Hash
                total = $results.Count
                passed = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'Passed' }).Count
                allPassed = @($results | Where-Object { $_.GetAttribute('outcome') -ne 'Passed' }).Count -eq 0
            }
        }
    }
    finally { $reader.Dispose() }
}

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$catalogPath = Join-Path $repository 'tests/CheatEngine.Mcp.Tests/Contract/Golden/backend-tools.json'
$evidencePath = Join-Path $repository 'docs/qualification/all-tools/behavior-evidence.json'
$liveEvidencePath = [IO.Path]::GetFullPath($LiveEvidencePath)
$catalog = @(Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json -AsHashtable)
$evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json -AsHashtable
$liveEvidence = Get-Content -LiteralPath $liveEvidencePath -Raw | ConvertFrom-Json -AsHashtable

function Outcome-Rank([string] $Outcome) {
    switch ($Outcome) {
        'Passed' { return 0 }
        'NotExecuted' { return 1 }
        'Skipped' { return 1 }
        'Inconclusive' { return 2 }
        'Failed' { return 3 }
        'Missing' { return 4 }
        'MissingOrDuplicate' { return 4 }
        default { return 3 }
    }
}

function Select-WorstOutcome([string[]] $Outcomes) {
    if ($Outcomes.Count -eq 0) { return 'Missing' }
    return @($Outcomes | Sort-Object { Outcome-Rank $_ } -Descending | Select-Object -First 1)[0]
}

function Get-TestOutcome($Report, $Case) {
    $outcomes = foreach ($name in @($Case['requiredCases'])) {
        if ($null -eq $Report -or -not $Report.Cases.ContainsKey($name) -or $Report.Occurrences[$name] -ne 1) {
            [ordered]@{ test = $name; outcome = 'MissingOrDuplicate' }
        }
        else {
            [ordered]@{ test = $name; outcome = $Report.Cases[$name] }
        }
    }
    return [ordered]@{ outcome = Select-WorstOutcome @($outcomes | ForEach-Object { $_['outcome'] }); cases = @($outcomes) }
}

function Get-ExactTestOutcome($Report, [string] $Method) {
    if ($null -eq $Report) { return 'MissingOrDuplicate' }
    $matches = @($Report.Cases.Keys | Where-Object {
        $_ -eq $Method -or ($_.StartsWith($Method, [StringComparison]::Ordinal) -and
            $_.Length -gt $Method.Length -and $_[$Method.Length] -eq '(')
    })
    if ($matches.Count -ne 1 -or $Report.Occurrences[$matches[0]] -ne 1) { return 'MissingOrDuplicate' }
    return $Report.Cases[$matches[0]]
}

function Assert-BehaviorEvidence($Evidence, $Catalog, [string] $Repository) {
    if ($Evidence['schema'] -ne 'cheatengine-mcp-behavior-evidence/v1') {
        throw "Unsupported behavior evidence schema '$($Evidence['schema'])'."
    }
    if ($null -eq $Evidence['boundary'] -or [string]::IsNullOrWhiteSpace([string] $Evidence['boundary'])) {
        throw 'Behavior evidence requires a non-empty boundary.'
    }
    $expected = @($Catalog | ForEach-Object { [string] $_['name'] })
    $tools = @($Evidence['tools'])
    $actual = @($tools | ForEach-Object { [string] $_['name'] })
    if ($actual.Count -ne $expected.Count -or @($actual | Sort-Object -Unique).Count -ne $actual.Count -or
        @(Compare-Object ($expected | Sort-Object) ($actual | Sort-Object)).Count -ne 0) {
        throw 'Behavior evidence tool names must be the exact unique golden backend catalog names.'
    }
    $root = [IO.Path]::GetFullPath($Repository).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    foreach ($tool in $tools) {
        if (-not $tool.ContainsKey('cases') -or -not $tool.ContainsKey('remaining')) {
            throw "Behavior evidence '$($tool['name'])' requires cases and remaining."
        }
        foreach ($case in @($tool['cases'])) {
            foreach ($key in @('suite', 'testMethod', 'requiredCases', 'sourceFile', 'scope', 'oracle')) {
                if (-not $case.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string] $case[$key])) {
                    throw "Behavior evidence '$($tool['name'])' has a case without $key."
                }
            }
            if ($case['suite'] -notin @('portable', 'nativeLua')) { throw "Unknown suite '$($case['suite'])'." }
            if ($case['scope'] -notin @('adapter', 'fixed_lua_body')) { throw "Unknown scope '$($case['scope'])'." }
            $requiredCases = @($case['requiredCases'])
            if ($requiredCases.Count -eq 0 -or @($requiredCases | Sort-Object -Unique).Count -ne $requiredCases.Count) {
                throw "Behavior evidence '$($tool['name'])' requires non-empty unique requiredCases."
            }
            foreach ($required in $requiredCases) {
                if ([string]::IsNullOrWhiteSpace([string] $required) -or
                    -not ($required -eq $case['testMethod'] -or ($required.StartsWith($case['testMethod'], [StringComparison]::Ordinal) -and
                        $required.Length -gt $case['testMethod'].Length -and $required[$case['testMethod'].Length] -eq '('))) {
                    throw "Behavior evidence '$($tool['name'])' has a required case outside testMethod '$($case['testMethod'])'."
                }
            }
            if ([IO.Path]::IsPathRooted([string] $case['sourceFile'])) { throw "sourceFile must be repository-relative." }
            $source = [IO.Path]::GetFullPath((Join-Path $Repository $case['sourceFile']))
            if (-not $source.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "sourceFile '$($case['sourceFile'])' is not an existing repository file."
            }
        }
    }
}

function Assert-LiveEvidence($LiveMap, $Catalog, [string] $Repository) {
    if ($LiveMap['schema'] -ne 'cheatengine-mcp-live-evidence/v1') {
        throw "Unsupported live evidence schema '$($LiveMap['schema'])'."
    }
    if ([string]::IsNullOrWhiteSpace([string] $LiveMap['boundary'])) { throw 'Live evidence requires a non-empty boundary.' }
    $names = @($LiveMap['tools'] | ForEach-Object { [string] $_['name'] })
    $catalogNames = @($Catalog | ForEach-Object { [string] $_['name'] })
    if ($names.Count -eq 0 -or @($names | Sort-Object -Unique).Count -ne $names.Count -or
        @($names | Where-Object { $_ -notin $catalogNames }).Count -ne 0) {
        throw 'Live evidence must contain a non-empty, unique subset of golden backend tool names.'
    }
    $root = [IO.Path]::GetFullPath($Repository).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    foreach ($entry in @($LiveMap['tools'])) {
        foreach ($key in @('name', 'sourceFile', 'testMethod', 'oracle', 'receipt', 'cleanupReceipt', 'scope')) {
            if (-not $entry.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string] $entry[$key])) {
                throw "Live evidence '$($entry['name'])' has no $key."
            }
        }
        if ($entry['scope'] -notin @('safe_gate', 'reversible_workflow', 'read_only_workflow')) {
            throw "Unknown live evidence scope '$($entry['scope'])'."
        }
        if ([IO.Path]::IsPathRooted([string] $entry['sourceFile'])) { throw 'Live sourceFile must be repository-relative.' }
        $source = [IO.Path]::GetFullPath((Join-Path $Repository $entry['sourceFile']))
        if (-not $source.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Live sourceFile '$($entry['sourceFile'])' is not an existing repository file."
        }
    }
}

function Read-LiveReceipts([string] $SummaryPath) {
    $path = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($SummaryPath))) 'receipts.jsonl'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Live receipt file '$path' is missing." }
    $counts = @{}
    foreach ($line in Get-Content -LiteralPath $path) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $entry = $line | ConvertFrom-Json -AsHashtable
        if (-not $entry.ContainsKey('name') -or [string]::IsNullOrWhiteSpace([string] $entry['name'])) { throw "Live receipt file '$path' has an unnamed row." }
        $name = [string] $entry['name']
        $prior = if ($counts.ContainsKey($name)) { [int] $counts[$name] } else { 0 }
        $counts[$name] = 1 + $prior
    }
    return @{ path = $path; sha256 = (Get-FileHash -LiteralPath $path).Hash; counts = $counts }
}

Assert-BehaviorEvidence $evidence $catalog $repository
Assert-LiveEvidence -LiveMap $liveEvidence -Catalog $catalog -Repository $repository
$gateway = Read-TestReport $GatewayTrx
$suiteReports = [ordered]@{ gateway = $gateway.Summary }
$behaviorReports = @{}
foreach ($entry in @(@{ Name = 'portable'; Path = $PortableTrx }, @{ Name = 'nativeLua'; Path = $NativeLuaTrx })) {
    if ($entry.Path) {
        $parsed = Read-TestReport $entry.Path
        $suiteReports[$entry.Name] = $parsed.Summary
        $behaviorReports[$entry.Name] = $parsed
    }
}
$live = if ($LiveSummary) { Get-Content -LiteralPath $LiveSummary -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
$liveReceipts = if ($LiveSummary) { Read-LiveReceipts $LiveSummary } else { $null }
$livePassed = $live['passed'] -eq $true -and $live['userStateRestored'] -eq $true -and
	$live['diagnosticOnly'] -ne $true -and
    $live['sourceInstallationUnchanged'] -eq $true -and $live.ContainsKey('cleanupFailures') -and
    @($live['cleanupFailures']).Count -eq 0 -and $live['schema'] -eq 'cheatengine-mcp-live-multi-instance/v1' -and
    $live.ContainsKey('selected_distribution') -and $live['selected_distribution'].ContainsKey('productVersion') -and
    $live['selected_distribution'].ContainsKey('pluginSha256') -and $live['selected_distribution'].ContainsKey('gatewaySha256')
$methods = @('CallTool_MissingInstanceId_RejectsEveryBackendToolBeforeAnyBackendContact',
    'CallTool_UnknownInstanceId_RejectsEveryBackendToolWithoutFallback',
    'CallTool_StaleIdentity_RejectsEveryBackendToolBeforeMutation',
    'CallTool_TwoInstances_RoutesEveryBackendToolWithOnlyInstanceIdRemoved')
$evidenceByTool = @{}
foreach ($entry in @($evidence['tools'])) { $evidenceByTool[$entry['name']] = $entry }
$liveEvidenceByTool = @{}
foreach ($entry in @($liveEvidence['tools'])) { $liveEvidenceByTool[$entry['name']] = $entry }
$tools = @($catalog) + @(@{ name = 'instance_list'; title = 'List instances'; _meta = @{} })
$rows = foreach ($tool in $tools | Sort-Object { $_['name'] }) {
    $name = $tool['name']
    $routingCases = if ($name -eq 'instance_list') {
        @('InstanceList_EmptyRegistry_ReturnsLocalEmptyDiscoveryResult', 'InstanceList_PublishedDoubles_ReturnsBothVerifiedInstancesWithoutTokens') |
            ForEach-Object { [ordered]@{ test = "CheatEngine.Mcp.Tests.Hosting.AllToolGatewayQualificationTests.$_"; outcome = Get-ExactTestOutcome $gateway "CheatEngine.Mcp.Tests.Hosting.AllToolGatewayQualificationTests.$_" } }
    } else {
        $methods | ForEach-Object {
            $method = "CheatEngine.Mcp.Tests.Hosting.AllToolGatewayQualificationTests.$_"
            $test = "$method(tool: `"$name`")"
            [ordered]@{ test = $test; outcome = Get-ExactTestOutcome $gateway $test }
        }
    }
    $sourceCases = if ($name -eq 'instance_list') { @() } else { @($evidenceByTool[$name]['cases']) }
    $asEvidence = foreach ($case in $sourceCases) {
        $testOutcome = Get-TestOutcome $behaviorReports[$case['suite']] $case
        [ordered]@{ suite = $case['suite']; test = $case['testMethod']; selectors = @($case['requiredCases']); sourceFile = $case['sourceFile']; scope = $case['scope']; oracle = $case['oracle']; outcome = $testOutcome['outcome']; caseOutcomes = $testOutcome['cases'] }
    }
    $adapterEvidence = @($asEvidence | Where-Object { $_['scope'] -eq 'adapter' })
    $fixedEvidence = @($asEvidence | Where-Object { $_['scope'] -eq 'fixed_lua_body' })
    $liveCase = $liveEvidenceByTool[$name]
    $liveReceiptPresent = $null -ne $liveCase -and $liveReceipts -and $liveReceipts.counts[$liveCase['receipt']] -eq 1
    $cleanupReceiptPresent = $null -ne $liveCase -and ($liveCase['cleanupReceipt'] -eq 'none' -or
        ($liveReceipts -and $liveReceipts.counts[$liveCase['cleanupReceipt']] -eq 1))
    [ordered]@{
        name = $name; title = $tool['title']; requiredFeatures = @($tool['_meta']['cheatengine/requires'] | Where-Object { $_ })
        routing = if (@($routingCases | Where-Object { $_.outcome -ne 'Passed' }).Count -eq 0) { 'passed' } else { 'incomplete' }
        routingCases = $routingCases
        behavior = if ($adapterEvidence.Count -gt 0 -and @($adapterEvidence | Where-Object { $_.outcome -ne 'Passed' }).Count -eq 0) { 'verified_with_doubles' } else { 'requires_reviewed_per_tool_evidence' }
        adapterEvidence = $adapterEvidence; fixedLuaBodyEvidence = $fixedEvidence
        remaining = if ($name -eq 'instance_list') { 'Local gateway discovery only; no backend behavior applies.' } else { $evidenceByTool[$name]['remaining'] }
        live = if ($null -eq $liveCase) { 'not_verified' } elseif ($livePassed -and $liveReceiptPresent -and $cleanupReceiptPresent) { 'verified' } elseif (-not $livePassed -and $liveReceiptPresent) { 'observed_in_failed_session' } else { 'not_verified' }
        liveEvidence = if ($null -eq $liveCase) { $null } else { [ordered]@{ scope = $liveCase['scope']; sourceFile = $liveCase['sourceFile']; testMethod = $liveCase['testMethod']; oracle = $liveCase['oracle']; receipt = $liveCase['receipt']; cleanupReceipt = $liveCase['cleanupReceipt']; receiptPresent = $liveReceiptPresent; cleanupReceiptPresent = $cleanupReceiptPresent } }
    }
}
$report = [ordered]@{
    schema = 'cheatengine-mcp-all-tools/v1'; generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    catalogSha256 = (Get-FileHash -LiteralPath $catalogPath).Hash; evidenceSha256 = (Get-FileHash -LiteralPath $evidencePath).Hash; liveEvidenceSha256 = (Get-FileHash -LiteralPath $liveEvidencePath).Hash
    toolCount = $rows.Count; routingComplete = @($rows | Where-Object { $_.routing -ne 'passed' }).Count -eq 0
    nativeQualificationComplete = $false
    boundaries = @('Routing uses loopback doubles; it does not execute backend tools.', 'Fixed Lua body evidence bypasses the adapter and never qualifies adapter behavior.', 'Suite totals never infer per-tool positive evidence.', 'Failed live sessions are not verified.', 'Missing tests are Missing; refusals do not qualify success.')
    suites = $suiteReports; liveSession = if ($LiveSummary) { [ordered]@{ summaryPath = [IO.Path]::GetFullPath($LiveSummary); summarySha256 = (Get-FileHash -LiteralPath $LiveSummary).Hash; receiptsPath = $liveReceipts.path; receiptsSha256 = $liveReceipts.sha256; passed = $livePassed; runId = $live['runId']; candidateIdentity = $live['selected_distribution']; host = $live['host']; ownedTarget = if ($live.ContainsKey('owned_target')) { $live['owned_target'] } else { $null } } } else { $null }
    tools = @($rows)
}
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
$output = Join-Path $OutputDirectory 'tool-results.json'
$report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $output -Encoding utf8
[pscustomobject]@{ Report = [IO.Path]::GetFullPath($output); Tools = $rows.Count; RoutingComplete = $report.routingComplete; NativeQualificationComplete = $false }
