#!/usr/bin/env pwsh
# Enforces the coverage gate: >= 90% line AND branch on PlanningPoker.Core.
# Runs Core.Tests with coverage, parses the cobertura report, and fails if below threshold.
param(
    [double]$Threshold = 0.90
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$results = Join-Path $here 'TestResults/coverage'

if (Test-Path $results) { Remove-Item $results -Recurse -Force }

dotnet test (Join-Path $here 'tests/PlanningPoker.Core.Tests') `
    --collect:"XPlat Code Coverage" `
    --results-directory $results
if ($LASTEXITCODE -ne 0) { Write-Error 'Core tests failed.'; exit 1 }

$report = Get-ChildItem $results -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
if (-not $report) { Write-Error 'No coverage report produced.'; exit 1 }

[xml]$xml = Get-Content $report.FullName
$pkg = $xml.coverage.packages.package | Where-Object { $_.name -eq 'PlanningPoker.Core' }
if (-not $pkg) { Write-Error 'PlanningPoker.Core not found in coverage report.'; exit 1 }

$line = [double]$pkg.'line-rate'
$branch = [double]$pkg.'branch-rate'
$thresholdPct = '{0:P0}' -f $Threshold

Write-Host ''
Write-Host "PlanningPoker.Core coverage — line: $('{0:P1}' -f $line), branch: $('{0:P1}' -f $branch) (gate: $thresholdPct)"

if ($line -lt $Threshold -or $branch -lt $Threshold) {
    Write-Error "Coverage gate FAILED: Core must be >= $thresholdPct line and branch."
    exit 1
}

Write-Host 'Coverage gate PASSED.' -ForegroundColor Green
