#!/usr/bin/env bash
# Генератор changelog из коммитов feat:/fix:/ui:/perf: (ТЗ §3.3).
# Коммиты chore:/build:/docs:/refactor: в релиз не попадают.
set -euo pipefail

VERSION="${1:-0.0.0}"
DATE="$(date +%Y-%m-%d)"

PREV_TAG="$(git describe --tags --abbrev=0 2>/dev/null || true)"
if [ -n "$PREV_TAG" ]; then RANGE="${PREV_TAG}..HEAD"; else RANGE="HEAD"; fi

collect() {
  git log --pretty=%s $RANGE | grep -E "^$1(\(.+\))?:" | sed -E "s/^$1(\(.+\))?:[ ]*/- /" || true
}

section() {
  local title="$1" prefix="$2" items
  items="$(collect "$prefix")"
  if [ -n "$items" ]; then
    echo "### $title"
    echo
    echo "$items"
    echo
  fi
}

echo "## YawaChatHub v$VERSION ($DATE)"
echo
section "Добавлено" "feat"
section "Исправлено" "fix"
section "Интерфейс" "ui"
section "Производительность" "perf"
