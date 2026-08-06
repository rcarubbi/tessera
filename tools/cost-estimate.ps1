<#
.SYNOPSIS
    Estimates LLM token cost for a repository by comparing graph snapshots.

    "Full pass" = summarizing every node from scratch.
    "Incremental" = only nodes whose content changed between the last two
    snapshots (the surface a re-run would actually re-summarize).

    Token estimate heuristic: 1 token per 4 characters of node content.

.PARAMETER ApiBase
    API base URL (default: $env:TESSERA_API or http://localhost:5080).

.PARAMETER ApiKey
    Dashboard API key, sent as Bearer (default: $env:TESSERA_API_KEY).

.PARAMETER Repo
    Optional repository full name filter (e.g. "acme/orders").

.PARAMETER PricePerMillionInput
    Optional USD per million input tokens to print an estimated cost.
#>
[CmdletBinding()]
param(
    [string]$ApiBase = $env:TESSERA_API,
    [string]$ApiKey = $env:TESSERA_API_KEY,
    [string]$Repo = '',
    [double]$PricePerMillionInput = 0
)

$ErrorActionPreference = 'Stop'

if (-not $ApiBase) { $ApiBase = 'http://localhost:5080' }
if (-not $ApiKey) { $ApiKey = '' }

function Invoke-TesseraApi {
    param([string]$Path)
    $headers = @{}
    if ($ApiKey) { $headers['Authorization'] = "Bearer $ApiKey" }
    return Invoke-RestMethod -Uri "$ApiBase$Path" -Headers $headers
}

function Get-TokenEstimate {
    param([object[]]$Nodes)
    $totalChars = 0
    foreach ($n in $Nodes) {
        if ($n.Content) { $totalChars += $n.Content.Length }
    }
    return [Math]::Ceiling($totalChars / 4)
}

$repos = Invoke-TesseraApi '/api/repositories'
if ($Repo) {
    $repos = @($repos | Where-Object { $_.fullName -eq $Repo })
}
if (-not $repos -or $repos.Count -eq 0) {
    Write-Host 'No repositories found.' -ForegroundColor Yellow
    exit 0
}

$rows = [System.Collections.Generic.List[object]]::new()
$totals = [ordered]@{ Full = 0; Incremental = 0 }

foreach ($repoItem in $repos) {
    $snapshots = Invoke-TesseraApi "/api/repositories/$($repoItem.id)/snapshots"
    if ($snapshots.Count -eq 0) {
        Write-Host "  $($repoItem.fullName): no snapshots" -ForegroundColor DarkGray
        continue
    }

    $latest = $snapshots[-1]
    $graphLatest = Invoke-TesseraApi "/api/repositories/$($repoItem.id)/graph?commit=$($latest.commitSha)"

    $fullTokens = Get-TokenEstimate @($graphLatest.nodes)
    $incrementalTokens = $fullTokens

    if ($snapshots.Count -ge 2) {
        $previous = $snapshots[-2]
        $graphPrev = Invoke-TesseraApi "/api/repositories/$($repoItem.id)/graph?commit=$($previous.commitSha)"
        $prevByKey = @{}
        foreach ($n in @($graphPrev.nodes)) { $prevByKey[$n.key] = $n.content }
        $changed = @($graphLatest.nodes | Where-Object {
            $prev = $prevByKey[$_.key]
            -not $prev -or $prev -ne $_.content
        })
        $incrementalTokens = Get-TokenEstimate $changed
    }

    $fullTokens = [int]$fullTokens
    $incrementalTokens = [int]$incrementalTokens
    $totals.Full += $fullTokens
    $totals.Incremental += $incrementalTokens

    $row = [pscustomobject]@{
        Repo           = $repoItem.fullName
        Snapshots      = $snapshots.Count
        Nodes          = @($graphLatest.nodes).Count
        FullTokens     = $fullTokens
        Incremental    = $incrementalTokens
        SavingsPercent = if ($fullTokens -gt 0) { [Math]::Round((1 - $incrementalTokens / $fullTokens) * 100, 1) } else { 0 }
    }
    $rows.Add($row)
    Write-Host ("  {0,-28} nodes={1,4}  full={2,8} tok  incr={3,8} tok  savings={4,5}%" -f `
        $row.Repo, $row.Nodes, $row.FullTokens, $row.Incremental, $row.SavingsPercent)
}

Write-Host ''
$totalSavings = if ($totals.Full -gt 0) { [Math]::Round((1 - $totals.Incremental / $totals.Full) * 100, 1) } else { 0 }
Write-Host ("Total: full={0} tokens, incremental={1} tokens ({2}% savings)" -f `
    $totals.Full, $totals.Incremental, $totalSavings)

if ($PricePerMillionInput -gt 0) {
    $fullCost = $totals.Full / 1e6 * $PricePerMillionInput
    $incrCost = $totals.Incremental / 1e6 * $PricePerMillionInput
    Write-Host ("Estimated cost @ {0}/MTok input: full={1:F4} USD, incremental={2:F4} USD" -f `
        $PricePerMillionInput, $fullCost, $incrCost)
}
