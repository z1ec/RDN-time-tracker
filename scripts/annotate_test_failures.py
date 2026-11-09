#!/usr/bin/env python3
"""Печатает упавшие тесты из TRX-отчётов как аннотации GitHub Actions (::error::).

Аннотации видны на странице запуска и через публичный API check-runs — без доступа к логам.
Использование: annotate_test_failures.py <папка с *.trx>
"""
import glob
import os
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def esc(s: str) -> str:
    return s.replace("%", "%25").replace("\r", "").replace("\n", "%0A")


count = 0
for path in glob.glob(os.path.join(sys.argv[1], "**", "*.trx"), recursive=True):
    for r in ET.parse(path).getroot().iterfind(".//t:UnitTestResult", NS):
        if r.get("outcome") != "Failed":
            continue
        count += 1
        msg = r.findtext(".//t:Message", default="", namespaces=NS)
        stack = r.findtext(".//t:StackTrace", default="", namespaces=NS)
        text = (msg + "\n" + "\n".join(stack.splitlines()[:6]))[:3000]
        print(f"::error title=Тест упал: {esc(r.get('testName', '?'))[:200]}::{esc(text)}")
print(f"Упавших тестов: {count}")
