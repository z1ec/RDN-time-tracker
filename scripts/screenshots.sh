#!/usr/bin/env bash
# Генерация скриншотов для README (headless Avalonia + Skia, демо-данные во временной БД).
# Результат: docs/images/*.png. Можно задать другую папку: SCREENSHOTS_DIR=/tmp/shots scripts/screenshots.sh
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test tests/WorkTimeTracker.App.Tests/WorkTimeTracker.App.Tests.csproj -c Release --filter "Category=Screenshots"
ls -l "${SCREENSHOTS_DIR:-docs/images}"/*.png
