#!/usr/bin/env bash
# Упаковка релиза из artifacts/win-x64/WorkTimeTracker.exe:
#   artifacts/release/WorkTimeTracker-<тег>-win-x64.exe, ...-win-x64.zip, SHA256SUMS.txt
# Использование: scripts/package-release.sh v1.0.0
set -euo pipefail
cd "$(dirname "$0")/.."
TAG="${1:?Укажите тег, например v1.0.0}"
EXE=artifacts/win-x64/WorkTimeTracker.exe
[ -f "$EXE" ] || { echo "Нет $EXE — сначала scripts/publish-win.sh"; exit 1; }

rm -rf artifacts/release artifacts/zip
mkdir -p artifacts/release artifacts/zip
cp "$EXE" "artifacts/release/WorkTimeTracker-$TAG-win-x64.exe"
cp "$EXE" LICENSE artifacts/zip/
if command -v 7z >/dev/null; then
  (cd artifacts/zip && 7z a -tzip "../release/WorkTimeTracker-$TAG-win-x64.zip" WorkTimeTracker.exe LICENSE > /dev/null)
else
  (cd artifacts/zip && zip -q "../release/WorkTimeTracker-$TAG-win-x64.zip" WorkTimeTracker.exe LICENSE)
fi
if command -v sha256sum >/dev/null; then SHA="sha256sum"; else SHA="shasum -a 256"; fi
(cd artifacts/release && $SHA "WorkTimeTracker-$TAG-win-x64.exe" "WorkTimeTracker-$TAG-win-x64.zip" > SHA256SUMS.txt)
cat artifacts/release/SHA256SUMS.txt
ls -l artifacts/release
