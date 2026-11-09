#!/usr/bin/env bash
# Заметки к релизу: секция CHANGELOG.md для версии + подсказка про запуск.
# Использование: scripts/release-notes.sh 1.0.0 > notes.md   (или Unreleased — для проверки в CI)
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:?Укажите версию, например 1.0.0}"
NOTES=$(awk -v ver="$VERSION" '
  index($0, "## [" ver "]") == 1 { found = 1; next }
  found && /^## \[/ { exit }
  found && /^\[[^]]+\]: / { exit }
  found { print }
' CHANGELOG.md)
if [ -z "$(echo "$NOTES" | tr -d '[:space:]')" ]; then
  echo "В CHANGELOG.md нет секции [$VERSION] или она пустая" >&2
  exit 1
fi
echo "$NOTES"
cat <<TXT

---
**Как запустить:** скачайте \`WorkTimeTracker-v$VERSION-win-x64.exe\` и запустите — установка и .NET не нужны.
Windows SmartScreen может предупредить о неизвестном издателе (exe не подписан): «Подробнее» → «Выполнить в любом случае».
Контрольные суммы — в \`SHA256SUMS.txt\`. Данные хранятся в \`%LOCALAPPDATA%\\WorkTimeTracker\`.
TXT
