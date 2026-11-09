#!/usr/bin/env bash
# Запуск приложения. Аргументы передаются как есть, например: scripts/run.sh --data-dir /tmp/wtt
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project src/WorkTimeTracker.App/WorkTimeTracker.App.csproj -c Debug -- "$@"
