# Mockups — per-target-launch-options

DESIGN-стадия для GUI-фичи «per-target-launch-options». Правило: >=4 стилистически разных
мокапа + approval **до** написания GUI-кода (BACKLOG P1-5).

## Варианты
- `01-minimal-light.html` — Minimal / Light
- `02-dark-pro.html` — Dark / Pro
- `03-high-contrast-a11y.html` — High-contrast / A11y
- `04-playful-rounded.html` — Playful / Rounded

## Как закрыть гейт
1. Доведи мокапы до реальных экранов (это скелеты-заглушки).
2. Обсуди/выбери направление с ревьюером.
3. Создай пустой файл `APPROVED` в этом каталоге (его проверяет hooks/design-gate.js).
4. Только после этого — реализация GUI.
