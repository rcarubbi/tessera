<#
.SYNOPSIS
    Scans git-tracked files for obvious secrets (private keys, API tokens).

    Exits non-zero when a match is found so it can gate CI.
    Uses `git ls-files` so generated/runtime files are never scanned.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$patterns = @(
    # Private keys / certificates
    '(?im)^\s*-----BEGIN (?:RSA |EC |OPENSSH |DSA |PGP )?PRIVATE KEY-----',
    # GitHub
    'ghp_[A-Za-z0-9]{30,}',
    'github_pat_[A-Za-z0-9_]{20,}',
    'gho_[A-Za-z0-9]{30,}',
    # OpenAI / Anthropic / DeepSeek-style keys
    'sk-[A-Za-z0-9_-]{16,}',
    'sk-ant-[A-Za-z0-9_-]{16,}',
    # Google API
    'AIza[0-9A-Za-z_-]{30,}',
    # AWS
    'AKIA[0-9A-Z]{16}',
    # Slack / generic long tokens
    'xox[baprs]-[0-9A-Za-z-]{10,}'
)

$binaryExts = @('.png', '.jpg', '.jpeg', '.gif', '.ico', '.woff', '.woff2', '.ttf', '.pdf', '.zip', '.dll', '.pdb', '.exe')

function Get-Relative {
    param([string]$FullName, [string]$Root)
    if (-not $Root.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $Root += [System.IO.Path]::DirectorySeparatorChar
    }
    return $FullName.Substring($Root.Length)
}

$repoRoot = $null
$previousEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$gitOutput = (git rev-parse --show-toplevel 2>$null | Out-String).Trim()
$ErrorActionPreference = $previousEap
if ($gitOutput -and -not ($gitOutput -like 'fatal:*')) {
    $repoRoot = $gitOutput
}
if (-not $repoRoot) {
    Write-Host 'WARN: not a git repo; scanning the working tree instead (git-tracked mode is stricter).' -ForegroundColor DarkYellow
    $repoRoot = (Get-Location).Path
    $excludeDirs = @('node_modules', '.next', 'bin', 'obj', 'dist', 'work', 'objects', '.git', '.opencode')
    $files = Get-ChildItem -Path $repoRoot -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $rel = (Get-Relative $_.FullName $repoRoot).Replace('\', '/')
            foreach ($dir in $excludeDirs) {
                if ($rel -like "*/$dir/*" -or $rel -eq $dir) { return $false }
            }
            return $true
        } |
        ForEach-Object { Get-Relative $_.FullName $repoRoot }
}
else {
    $files = git ls-files -z | ForEach-Object { $_ }
    $files = $files -split "`0" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

$matches = [System.Collections.Generic.List[object]]::new()

foreach ($file in $files) {
    if ($binaryExts -contains [System.IO.Path]::GetExtension($file).ToLowerInvariant()) { continue }
    $fullPath = Join-Path $repoRoot $file
    if (-not (Test-Path -LiteralPath $fullPath)) { continue }

    $lineNo = 0
    foreach ($line in [System.IO.File]::ReadLines($fullPath)) {
        $lineNo++
        foreach ($pattern in $patterns) {
            if ($line -match $pattern) {
                $matches.Add([pscustomobject]@{ File = $file; Line = $lineNo; Pattern = $pattern })
                break
            }
        }
    }
}

if ($matches.Count -gt 0) {
    Write-Host "FAIL: $($matches.Count) possible secret(s) found in tracked files:" -ForegroundColor Red
    $matches | ForEach-Object {
        Write-Host "  $($_.File):$($_.Line)  (pattern: $($_.Pattern))" -ForegroundColor Yellow
    }
    exit 1
}

Write-Host 'OK: no obvious secrets in tracked files.' -ForegroundColor Green
exit 0
