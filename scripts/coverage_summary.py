#!/usr/bin/env python3
"""Сводка покрытия из отчётов coverlet (cobertura) + проверка порога для WorkTimeTracker.Core.

Использование: coverage_summary.py <папка TestResults> <порог в %>
Печатает Markdown-таблицу; если задан GITHUB_STEP_SUMMARY — дописывает её туда.
Код выхода 1, если покрытие строк Core ниже порога.
"""
import glob
import os
import sys
import xml.etree.ElementTree as ET

results_dir, threshold = sys.argv[1], float(sys.argv[2])

# Пакет может встречаться в нескольких отчётах (каждый тестовый проект) — берём лучший результат
# по каждой сборке, т.к. отчёты считаются по разным наборам тестов.
packages: dict[str, tuple[int, int]] = {}
for path in glob.glob(os.path.join(results_dir, "**", "coverage.cobertura.xml"), recursive=True):
    for pkg in ET.parse(path).getroot().iter("package"):
        name = pkg.get("name", "?")
        # Строки берём только из class/lines: в methods/method/lines они дублируются.
        hits: dict[tuple[str, str], bool] = {}
        for cls in pkg.iter("class"):
            lines_el = cls.find("lines")
            if lines_el is None:
                continue
            for line in lines_el.findall("line"):
                key = (cls.get("filename", ""), line.get("number", ""))
                hits[key] = hits.get(key, False) or int(line.get("hits", "0")) > 0
        total, covered = len(hits), sum(hits.values())
        prev = packages.get(name)
        if prev is None or covered * max(prev[1], 1) > prev[0] * max(total, 1):
            packages[name] = (covered, total)

if not packages:
    print("Отчёты покрытия не найдены", file=sys.stderr)
    sys.exit(1)

rows = ["| Сборка | Покрытие строк | Строк |", "|---|---:|---:|"]
for name, (covered, total) in sorted(packages.items()):
    pct = 100.0 * covered / total if total else 100.0
    rows.append(f"| {name} | {pct:.1f}% | {covered}/{total} |")

core = packages.get("WorkTimeTracker.Core")
core_pct = 100.0 * core[0] / core[1] if core and core[1] else 0.0
status = "✅" if core_pct >= threshold else "❌"
summary = "\n".join(["## Покрытие кода", "", *rows, "",
                     f"{status} Порог для WorkTimeTracker.Core: {threshold:.0f}% (факт {core_pct:.1f}%)", ""])
print(summary)
step_summary = os.environ.get("GITHUB_STEP_SUMMARY")
if step_summary:
    with open(step_summary, "a", encoding="utf-8") as f:
        f.write(summary + "\n")
sys.exit(0 if core_pct >= threshold else 1)
