#requires -Version 7.0
<#
.SYNOPSIS
Writes one evidence row per advertised tool without treating routing or refusals as native qualification.

.EXAMPLE
$liveSummaries = @('C:\qualification\workflow\summary.json', 'C:\qualification\smoke\summary.json')
.\eng\Write-AllToolQualificationReport.ps1 -GatewayTrx .\gateway.trx -LiveSummaries $liveSummaries
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $GatewayTrx,
    [string] $PortableTrx,
    [string] $NativeLuaTrx,
    [string] $LiveSummary,
    [string[]] $LiveSummaries,
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
    $catalogNames = @($Catalog | ForEach-Object { [string] $_['name'] }) + @('instance_list')
    if ($names.Count -eq 0 -or @($names | Sort-Object -Unique).Count -ne $names.Count -or
        @($names | Where-Object { $_ -notin $catalogNames }).Count -ne 0) {
        throw 'Live evidence must contain a non-empty, unique subset of advertised tool names.'
    }
    $root = [IO.Path]::GetFullPath($Repository).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    foreach ($entry in @($LiveMap['tools'])) {
        foreach ($key in @('name', 'sourceFile', 'testMethod', 'oracle', 'receipt', 'cleanupReceipt', 'scope')) {
            if (-not $entry.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string] $entry[$key])) {
                throw "Live evidence '$($entry['name'])' has no $key."
            }
        }
        if ($entry['scope'] -notin @('safe_gate', 'reversible_workflow', 'read_only_workflow', 'gateway_discovery')) {
            throw "Unknown live evidence scope '$($entry['scope'])'."
        }
        if (($entry['name'] -eq 'instance_list') -ne ($entry['scope'] -eq 'gateway_discovery')) {
            throw 'Only instance_list uses the gateway_discovery live scope.'
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

function Read-LiveSession([string] $SummaryPath) {
    $summaryPath = [IO.Path]::GetFullPath($SummaryPath)
    $live = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in @('schema', 'runId', 'passed', 'userStateRestored', 'sourceInstallationUnchanged', 'cleanupFailures', 'selected_distribution')) {
        if (-not $live.ContainsKey($key)) { throw "Live summary '$summaryPath' has no $key." }
    }
    if ($live['schema'] -ne 'cheatengine-mcp-live-multi-instance/v1') {
        throw "Live summary '$summaryPath' has unsupported schema '$($live['schema'])'."
    }
    if ([string]::IsNullOrWhiteSpace([string] $live['runId'])) { throw "Live summary '$summaryPath' has no runId." }
    if ($live['cleanupFailures'] -isnot [System.Collections.IEnumerable] -or $live['cleanupFailures'] -is [string]) {
        throw "Live summary '$summaryPath' has invalid cleanupFailures."
    }
    $identity = $live['selected_distribution']
    foreach ($key in @('productVersion', 'pluginSha256', 'gatewaySha256')) {
        if (-not $identity.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string] $identity[$key])) {
            throw "Live summary '$summaryPath' has no selected_distribution.$key."
        }
    }
    foreach ($key in @('pluginSha256', 'gatewaySha256')) {
        if ([string] $identity[$key] -notmatch '^[0-9a-fA-F]{64}$') {
            throw "Live summary '$summaryPath' has invalid selected_distribution.$key."
        }
    }
    # Pre-flag summaries from the two diagnostic runners recorded these exact keys without a
    # top-level discriminator. Treat them as diagnostic evidence; never promote them to qualification.
    $legacyDiagnosticMarkers = @(
        'dispatch_diagnostic_case',
        'dispatch_diagnostic_prelude',
        'dispatch_diagnostic_step',
        'dispatch_diagnostic_gateway_cleanup_failure'
    )
    $diagnosticOnly = $live['diagnosticOnly'] -eq $true -or
        @($legacyDiagnosticMarkers | Where-Object { $live.ContainsKey($_) }).Count -gt 0
    $passed = $live['passed'] -eq $true -and $live['userStateRestored'] -eq $true -and
        $live['sourceInstallationUnchanged'] -eq $true -and @($live['cleanupFailures']).Count -eq 0 -and -not $diagnosticOnly
    return @{ summaryPath = $summaryPath; summarySha256 = (Get-FileHash -LiteralPath $summaryPath).Hash;
        receipts = Read-LiveReceipts $summaryPath; passed = $passed; diagnosticOnly = $diagnosticOnly;
        runId = [string] $live['runId']; candidateIdentity = $identity; host = $live['host'];
        ownedTarget = if ($live.ContainsKey('owned_target')) { $live['owned_target'] } else { $null } }
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
if ($PSBoundParameters.ContainsKey('LiveSummary') -and $PSBoundParameters.ContainsKey('LiveSummaries')) {
    throw 'Specify either LiveSummary or LiveSummaries, not both.'
}
$requestedLiveSummaries = if ($PSBoundParameters.ContainsKey('LiveSummary')) {
    if ($null -eq $LiveSummary) { @() } else { @($LiveSummary) }
}
elseif ($PSBoundParameters.ContainsKey('LiveSummaries')) {
    if ($null -eq $LiveSummaries) { @() } else { @($LiveSummaries | Where-Object { $null -ne $_ }) }
}
else { @() }
$canonicalSummaryPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$runIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$liveSessions = @()
foreach ($summary in $requestedLiveSummaries) {
    if ([string]::IsNullOrWhiteSpace($summary)) { throw 'Live summary paths must be non-empty.' }
    $session = Read-LiveSession $summary
    if (-not $canonicalSummaryPaths.Add($session.summaryPath)) { throw "Duplicate live summary path '$($session.summaryPath)'." }
    if (-not $runIds.Add($session.runId)) { throw "Duplicate live runId '$($session.runId)'." }
    $liveSessions += $session
}
if ($liveSessions.Count -gt 1) {
    $candidate = $liveSessions[0].candidateIdentity
    foreach ($session in @($liveSessions | Select-Object -Skip 1)) {
        foreach ($key in @('productVersion', 'pluginSha256', 'gatewaySha256')) {
            $comparison = if ($key -eq 'productVersion') { [StringComparison]::Ordinal } else { [StringComparison]::OrdinalIgnoreCase }
            if (-not [string]::Equals([string] $candidate[$key], [string] $session.candidateIdentity[$key], $comparison)) {
                throw "Live summaries select different candidate bundles ($key differs)."
            }
        }
    }
}
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
    $liveSessionEvidence = if ($null -eq $liveCase) { @() } else { @($liveSessions | ForEach-Object {
        $receiptPresent = $_.receipts.counts[$liveCase['receipt']] -eq 1
        $cleanupRequired = $liveCase['cleanupReceipt'] -ne 'none'
        $cleanupReceiptPresent = if ($cleanupRequired) { $_.receipts.counts[$liveCase['cleanupReceipt']] -eq 1 } else { $false }
        $cleanupSatisfied = -not $cleanupRequired -or $cleanupReceiptPresent
        [ordered]@{ runId = $_.runId; summarySha256 = $_.summarySha256; receiptPresent = $receiptPresent;
            cleanupRequired = $cleanupRequired; cleanupReceiptPresent = $cleanupReceiptPresent; cleanupSatisfied = $cleanupSatisfied;
            passed = $_.passed; diagnosticOnly = $_.diagnosticOnly }
    }) }
    $verifiedLiveSession = @($liveSessionEvidence | Where-Object { $_.passed -and $_.receiptPresent -and $_.cleanupSatisfied })
    $diagnosticLiveSession = @($liveSessionEvidence | Where-Object { $_.diagnosticOnly -and $_.receiptPresent })
    $failedLiveSession = @($liveSessionEvidence | Where-Object { -not $_.passed -and -not $_.diagnosticOnly -and $_.receiptPresent })
    [ordered]@{
        name = $name; title = $tool['title']; requiredFeatures = @($tool['_meta']['cheatengine/requires'] | Where-Object { $_ })
        routing = if (@($routingCases | Where-Object { $_.outcome -ne 'Passed' }).Count -eq 0) { 'passed' } else { 'incomplete' }
        routingCases = $routingCases
        behavior = if ($adapterEvidence.Count -gt 0 -and @($adapterEvidence | Where-Object { $_.outcome -ne 'Passed' }).Count -eq 0) { 'verified_with_doubles' } else { 'requires_reviewed_per_tool_evidence' }
        adapterEvidence = $adapterEvidence; fixedLuaBodyEvidence = $fixedEvidence
        remaining = if ($name -eq 'instance_list') { 'Local gateway discovery only; no backend behavior applies.' }
            elseif ($verifiedLiveSession.Count -gt 0) { 'The listed native case passed on the recorded bundle; wider behavior and environment coverage remain unverified.' }
            else { $evidenceByTool[$name]['remaining'] }
        live = if ($null -eq $liveCase) { 'not_verified' } elseif ($verifiedLiveSession.Count -gt 0) { 'verified' } elseif ($diagnosticLiveSession.Count -gt 0) { 'diagnostic_only' } elseif ($failedLiveSession.Count -gt 0) { 'observed_in_failed_session' } else { 'not_verified' }
        liveEvidence = if ($null -eq $liveCase) { $null } else { [ordered]@{ scope = $liveCase['scope']; sourceFile = $liveCase['sourceFile']; testMethod = $liveCase['testMethod']; oracle = $liveCase['oracle']; receipt = $liveCase['receipt']; cleanupReceipt = $liveCase['cleanupReceipt']; sessions = @($liveSessionEvidence) } }
    }
}
$report = [ordered]@{
    schema = 'cheatengine-mcp-all-tools/v1'; generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    catalogSha256 = (Get-FileHash -LiteralPath $catalogPath).Hash; evidenceSha256 = (Get-FileHash -LiteralPath $evidencePath).Hash; liveEvidenceSha256 = (Get-FileHash -LiteralPath $liveEvidencePath).Hash
    toolCount = $rows.Count; routingComplete = @($rows | Where-Object { $_.routing -ne 'passed' }).Count -eq 0
    nativeQualificationComplete = $false
    boundaries = @('Routing uses loopback doubles; it does not execute backend tools.', 'Fixed Lua body evidence bypasses the adapter and never qualifies adapter behavior.', 'Suite totals never infer per-tool positive evidence.', 'A live success receipt and, when required, exactly one mapped cleanup receipt qualify only in the same passing session.', 'When cleanupReceipt is none, cleanupRequired is false and cleanupSatisfied is true; no cleanup receipt is claimed.', 'Failed or diagnostic-only live sessions are not verified.', 'Missing tests are Missing; refusals do not qualify success.')
    suites = $suiteReports
    liveSessions = @($liveSessions | ForEach-Object { [ordered]@{ summaryPath = $_.summaryPath; summarySha256 = $_.summarySha256; receiptsPath = $_.receipts.path; receiptsSha256 = $_.receipts.sha256; passed = $_.passed; runId = $_.runId; candidateIdentity = $_.candidateIdentity; host = $_.host; ownedTarget = $_.ownedTarget } })
    liveSession = if ($liveSessions.Count -eq 1) { [ordered]@{ summaryPath = $liveSessions[0].summaryPath; summarySha256 = $liveSessions[0].summarySha256; receiptsPath = $liveSessions[0].receipts.path; receiptsSha256 = $liveSessions[0].receipts.sha256; passed = $liveSessions[0].passed; runId = $liveSessions[0].runId; candidateIdentity = $liveSessions[0].candidateIdentity; host = $liveSessions[0].host; ownedTarget = $liveSessions[0].ownedTarget } } else { $null }
    tools = @($rows)
}
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
$output = Join-Path $OutputDirectory 'tool-results.json'
$report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $output -Encoding utf8
[pscustomobject]@{ Report = [IO.Path]::GetFullPath($output); Tools = $rows.Count; RoutingComplete = $report.routingComplete; NativeQualificationComplete = $false }
