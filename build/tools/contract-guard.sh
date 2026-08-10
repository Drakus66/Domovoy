#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2025-2026 Ilya Dryagin
# This file is part of Domovoy, licensed under AGPL-3.0-or-later. See LICENSE.
#
# Страж контракта (Эпик 3K).
#
# Domovoy обновляется покомпонентно, и решатель зависимостей доверяет ровно тому, что
# объявлено в build/components.json. Изменение контракта без объявления — единственный
# способ тихо получить несовместимое обновление на боевом доме, и заметить это глазами
# в диффе владелец не обязан.
#
# Правило: если диапазон коммитов трогает контрактные пути, он обязан либо изменить
# build/components.json, либо нести явное подтверждение `Compat: none` в теле коммита.
# Оба варианта видны в истории, поэтому аудит дешёвый:
#     git log --grep='^Compat: none'
#
# Использование:
#     build/tools/contract-guard.sh <base-ref> <head-ref>
#     build/tools/contract-guard.sh HEAD~1 HEAD        # локально, перед коммитом

set -euo pipefail

BASE="${1:-}"
HEAD_REF="${2:-HEAD}"

if [ -z "$BASE" ] || ! git rev-parse --verify --quiet "$BASE" >/dev/null; then
  echo "contract-guard: базовая ревизия '$BASE' недоступна — проверка пропущена."
  echo "Так бывает на первом пуше ветки: сравнивать не с чем."
  exit 0
fi

# Пути, изменение которых способно поменять совместимость.
#
# src/Plugins/*/plugin.json — объявленная поверхность плагина (id, capability, подписки, схема
# настроек, ресурсные требования). Плагины едут внутри образа plugin-supervisor, поэтому их
# манифест — такой же контракт с супервизором и UI, как Endpoints/ у сервиса.
CONTRACT_PATTERNS='^(src/Common/Domovoy\.Contracts/|src/Gateway/[^/]+/Endpoints/|src/Gateway/[^/]+/Controllers/|src/Services/[^/]+/Endpoints/|src/Services/[^/]+/Controllers/|src/Plugins/[^/]+/plugin\.json$|docker-compose\.yml$)'

CHANGED="$(git diff --name-only "$BASE" "$HEAD_REF")"
TOUCHED="$(printf '%s\n' "$CHANGED" | grep -E "$CONTRACT_PATTERNS" || true)"

if [ -z "$TOUCHED" ]; then
  echo "contract-guard: контрактные пути не затронуты."
  exit 0
fi

if printf '%s\n' "$CHANGED" | grep -qx 'build/components\.json'; then
  echo "contract-guard: контракт затронут, build/components.json обновлён — порядок."
  exit 0
fi

# Осознанный обход: автор утверждает, что изменение обратно совместимо.
if git log --format=%B "$BASE..$HEAD_REF" | grep -qE '^Compat: none[[:space:]]*$'; then
  echo "contract-guard: контракт затронут, но коммит несёт 'Compat: none' — принято."
  echo "Все такие случаи: git log --grep='^Compat: none'"
  exit 0
fi

cat >&2 <<EOF

contract-guard: изменения затрагивают контракт, но build/components.json не менялся.

Затронуто:
$(printf '%s\n' "$TOUCHED" | sed 's/^/  /')

Нужно одно из двух:

  1. Объявить изменение в build/components.json:
       • шина (Domovoy.Contracts): ломающее изменение payload/типа → новый .vN в MessageTypes
         И поднять bus.speaks/bus.understands затронутых компонентов; обратно совместимое
         добавление → только speaks;
       • HTTP между сервисами: добавили эндпоинт или поле → provides.<интерфейс>.version +1;
         убрали или изменили семантику → ещё и minCompat = новой версии; начали пользоваться
         новым эндпоинтом → requires.<интерфейс> у потребителя;
       • топология: новый сервис/env/том/порт → поднять build/topology/manifest.json version
         и добавить requires.topology тем, кому новое поле нужно.

  2. Если изменение обратно совместимо и версий поднимать не надо — подтвердить это явно,
     добавив в тело коммита отдельную строку:

       Compat: none

Правила целиком: docs/architecture/coding_standards_ru.md, раздел «Контракты и совместимость».

EOF
exit 1
