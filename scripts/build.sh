#!/usr/bin/env bash
# Сборка решения в Release (предупреждения = ошибки).
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore --locked-mode
dotnet build -c Release --no-restore
