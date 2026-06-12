#!/usr/bin/env bash
#
# One-command runner for Planning Poker.
#   ./run.sh        dev   (default) - API (:5210) + Angular dev server (:4200) together
#   ./run.sh prod         - publish the single artifact (API + SPA) and run it on :5210
#   ./run.sh test         - run all backend tests, the Core coverage gate, and frontend tests
#
set -euo pipefail

MODE="${1:-dev}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKEND="$ROOT/backend"
FRONTEND="$ROOT/frontend"
API_PROJECT="$BACKEND/src/PlanningPoker.Api"
SOLUTION="$BACKEND/PlanningPoker.slnx"

require() {
  command -v "$1" >/dev/null 2>&1 || { echo "Error: required tool '$1' not found on PATH. $2"; exit 1; }
}

ensure_frontend_deps() {
  if [ ! -d "$FRONTEND/node_modules" ]; then
    echo "==> Installing frontend dependencies (npm install)..."
    (cd "$FRONTEND" && npm install)
  fi
}

# Kill any process already listening on a port (a leftover API/dev-server from a previous run),
# so a fresh start doesn't hit "address already in use" and end up talking to a stale instance.
free_port() {
  local port="$1"
  local pids=""
  case "$(uname -s)" in
    MINGW* | MSYS* | CYGWIN*)
      pids="$(netstat -ano 2>/dev/null | grep -E ":$port[[:space:]].*LISTENING" | awk '{print $NF}' | sort -u || true)"
      for pid in $pids; do
        [ -n "$pid" ] || continue
        echo "==> Freeing port $port (stopping PID $pid)"
        # taskkill is usually enough; fall back to PowerShell Stop-Process if it can't.
        taskkill //F //PID "$pid" >/dev/null 2>&1 \
          || powershell.exe -NoProfile -Command "Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue" >/dev/null 2>&1 \
          || true
      done
      ;;
    *)
      if command -v lsof >/dev/null 2>&1; then
        pids="$(lsof -ti "tcp:$port" 2>/dev/null || true)"
        for pid in $pids; do
          [ -n "$pid" ] && { echo "==> Freeing port $port (stopping PID $pid)"; kill -9 "$pid" 2>/dev/null || true; }
        done
      fi
      ;;
  esac
}

echo "Planning Poker — mode: $MODE"
require dotnet "Install the .NET 10 SDK: https://dotnet.microsoft.com/download"
require node   "Install Node 20+: https://nodejs.org"
require npm    "npm ships with Node.js"

case "$MODE" in
  test)
    echo "==> Backend tests..."
    dotnet test "$SOLUTION"
    if command -v pwsh >/dev/null 2>&1; then
      echo "==> Coverage gate (Core >= 90%)..."
      pwsh -File "$BACKEND/coverage-gate.ps1"
    else
      echo "(!) pwsh not found — skipping coverage gate (install PowerShell to enable it)."
    fi
    echo "==> Frontend tests..."
    ensure_frontend_deps
    (cd "$FRONTEND" && npm test -- --watch=false)
    echo "All tests passed."
    ;;

  prod)
    echo "==> Publishing single artifact (builds Angular into wwwroot)..."
    dotnet publish "$API_PROJECT" -c Release -o "$ROOT/publish"
    free_port 5210
    echo "==> Starting app on http://localhost:5210 (Ctrl+C to stop)"
    # --contentRoot so appsettings*.json load (config is read relative to the content root, not the DLL).
    ASPNETCORE_URLS=http://localhost:5210 dotnet "$ROOT/publish/PlanningPoker.Api.dll" --contentRoot "$ROOT/publish"
    ;;

  dev)
    ensure_frontend_deps
    # Stop any leftover servers from a previous run before we start fresh.
    free_port 5210
    free_port 4200
    echo "==> Building backend..."
    dotnet build "$API_PROJECT" -c Debug

    # Run the built DLL directly (not 'dotnet run', which forks a child we couldn't cleanly kill),
    # so $API_PID is the actual server and shutdown stops it reliably.
    API_DLL="$API_PROJECT/bin/Debug/net10.0/PlanningPoker.Api.dll"
    echo "==> Starting API on http://localhost:5210 ..."
    # --contentRoot points at the project so appsettings*.json load (config is read relative to the
    # content root, not the DLL's folder — without this, Integrations:Enabled etc. silently default).
    ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5210 \
      dotnet "$API_DLL" --contentRoot "$API_PROJECT" &
    API_PID=$!

    cleanup() {
      echo
      echo "==> Stopping..."
      kill "$API_PID" 2>/dev/null || true
      wait "$API_PID" 2>/dev/null || true
    }
    trap cleanup EXIT INT TERM

    echo "==> Starting Angular dev server on http://localhost:4200 (Ctrl+C to stop both)"
    (cd "$FRONTEND" && npm start)
    ;;

  *)
    echo "Unknown mode '$MODE'. Use: dev | prod | test"
    exit 1
    ;;
esac
