#!/usr/bin/env pwsh
<#
.SYNOPSIS
  One-command runner for Planning Poker.
.DESCRIPTION
  Checks prerequisites, installs/restores dependencies, then:
    dev   (default) - starts the API (:5210) and the Angular dev server (:4200) together
    prod            - publishes the single artifact (API + SPA) and runs it on :5210
    test            - runs all backend tests, the Core coverage gate, and frontend tests
.EXAMPLE
  ./run.ps1            # dev
  ./run.ps1 prod
  ./run.ps1 test
#>
param(
    [ValidateSet('dev', 'prod', 'test')]
    [string]$Mode = 'dev'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$backend = Join-Path $root 'backend'
$frontend = Join-Path $root 'frontend'
$apiProject = Join-Path $backend 'src/PlanningPoker.Api'
$solution = Join-Path $backend 'PlanningPoker.slnx'

function Require-Command($name, $hint) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "Required tool '$name' was not found on PATH. $hint"
    }
}

function Ensure-FrontendDeps {
    if (-not (Test-Path (Join-Path $frontend 'node_modules'))) {
        Write-Host '==> Installing frontend dependencies (npm install)...' -ForegroundColor Cyan
        Push-Location $frontend
        try { npm install } finally { Pop-Location }
    }
}

# Kill any process already listening on a port (a leftover API/dev-server from a previous run), so a
# fresh start doesn't hit "address already in use" and end up talking to a stale instance.
function Free-Port($port) {
    try {
        $owningPids = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop |
            Select-Object -ExpandProperty OwningProcess -Unique
    }
    catch {
        # Get-NetTCPConnection unavailable or no listener — fall back to netstat parsing.
        $owningPids = (netstat -ano | Select-String ":$port\s.*LISTENING") -replace '.*\s(\d+)$', '$1' | Sort-Object -Unique
    }
    foreach ($processId in $owningPids) {
        if ($processId -and $processId -ne '0') {
            Write-Host "==> Freeing port $port (stopping PID $processId)" -ForegroundColor Cyan
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host "Planning Poker — mode: $Mode" -ForegroundColor Green
Require-Command dotnet 'Install the .NET 10 SDK: https://dotnet.microsoft.com/download'
Require-Command node   'Install Node 20+: https://nodejs.org'
Require-Command npm    'npm ships with Node.js'

switch ($Mode) {
    'test' {
        Write-Host '==> Backend tests...' -ForegroundColor Cyan
        dotnet test $solution
        Write-Host '==> Coverage gate (Core >= 90%)...' -ForegroundColor Cyan
        & (Join-Path $backend 'coverage-gate.ps1')
        Write-Host '==> Frontend tests...' -ForegroundColor Cyan
        Push-Location $frontend
        try { Ensure-FrontendDeps; npm test -- --watch=false } finally { Pop-Location }
        Write-Host 'All tests passed.' -ForegroundColor Green
        return
    }

    'prod' {
        $publish = Join-Path $root 'publish'
        Write-Host '==> Publishing single artifact (builds Angular into wwwroot)...' -ForegroundColor Cyan
        dotnet publish $apiProject -c Release -o $publish
        Free-Port 5210
        Write-Host '==> Starting app on http://localhost:5210 (Ctrl+C to stop)' -ForegroundColor Green
        $env:ASPNETCORE_URLS = 'http://localhost:5210'
        Push-Location $publish
        # --contentRoot so appsettings*.json load (config is read relative to the content root).
        try { dotnet PlanningPoker.Api.dll --contentRoot $publish } finally { Pop-Location }
        return
    }

    default {
        # dev: API + Angular dev server together
        Ensure-FrontendDeps
        # Stop any leftover servers from a previous run before we start fresh.
        Free-Port 5210
        Free-Port 4200
        Write-Host '==> Building backend...' -ForegroundColor Cyan
        dotnet build $apiProject -c Debug

        # Run the built DLL directly (not 'dotnet run', which forks a child we couldn't cleanly
        # kill), so the tracked process IS the server and shutdown stops it reliably.
        $apiDll = Join-Path $apiProject 'bin/Debug/net10.0/PlanningPoker.Api.dll'
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:ASPNETCORE_URLS = 'http://localhost:5210'
        Write-Host '==> Starting API on http://localhost:5210 ...' -ForegroundColor Cyan
        # --contentRoot points at the project so appsettings*.json load (config is read relative to the
        # content root, not the DLL's folder — without this, Integrations:Enabled etc. silently default).
        $api = Start-Process dotnet -ArgumentList @($apiDll, '--contentRoot', $apiProject) -PassThru -NoNewWindow

        try {
            Write-Host '==> Starting Angular dev server on http://localhost:4200 (Ctrl+C to stop both)' -ForegroundColor Green
            Push-Location $frontend
            try { npm start } finally { Pop-Location }
        }
        finally {
            if ($api -and -not $api.HasExited) {
                Write-Host '==> Stopping API...' -ForegroundColor Cyan
                Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
