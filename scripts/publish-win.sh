#!/usr/bin/env bash
# Публикация self-contained single-file exe для Windows x64 (работает и на macOS/Linux).
# Использование: scripts/publish-win.sh [версия]   → artifacts/win-x64/WorkTimeTracker.exe
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:-${VERSION:-1.0.0}}"
dotnet publish src/WorkTimeTracker.App/WorkTimeTracker.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=none \
  -p:Version="$VERSION" -o artifacts/win-x64
ls -lh artifacts/win-x64
