#!/usr/bin/env bash
# Все тесты (кроме генерации скриншотов) с покрытием + проверка порога покрытия Core.
# Переменные: COVERAGE_THRESHOLD (по умолчанию 90), BUILD_ARGS (например "--no-build").
set -euo pipefail
cd "$(dirname "$0")/.."
THRESHOLD="${COVERAGE_THRESHOLD:-90}"
rm -rf TestResults
# shellcheck disable=SC2086
dotnet test WorkTimeTracker.slnx -c Release ${BUILD_ARGS:-} \
  --filter "Category!=Screenshots" \
  --collect "XPlat Code Coverage" --results-directory TestResults \
  --logger "trx" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.ExcludeByAttribute=GeneratedCodeAttribute,CompilerGeneratedAttribute
python3 scripts/coverage_summary.py TestResults "$THRESHOLD"
