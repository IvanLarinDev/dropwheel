# tests/Dropwheel.Tests — заметка для нейросети

Роль папки: весь xUnit-набор проекта. Тестируется чистая логика (`Services`, `Models`,
`internal static`-помощники из `UI`) без запуска окон; WPF-типы можно трогать только
статически (пример: `OverlayWindow.FormatBytes`, `SettingsWindow.HotkeyChipState`).

## Соглашения

- Плоские `sealed`-классы, по файлу на тему. Без наследования и общих фикстур.
- Имена тестов — предложения в snake-стиле xUnit: `Valid_file_replaces_the_config`.
- Доступ к `internal` — через `InternalsVisibleTo` в `src/Dropwheel/Dropwheel.csproj`.
- Всё, что трогает статический `TargetStore` (Load/Save/Config), обязано носить
  `[Collection("TargetStoreState")]` — коллекция сериализует такие тесты, иначе
  параллельный прогон даёт гонки за глобальное состояние. Папка конфига подменяется
  `TargetStore.DirOverride` (временная, с `Guid` в имени), в `Dispose` — сброс
  override и удаление папки.

## Локальные запреты

- Не запускать окна и диспетчеры WPF — логика для тестов выносится в чистые
  `internal`-функции на стороне приложения.
- Не писать в реальный `%APPDATA%\Dropwheel` — только `DirOverride`.
- Не полагаться на порядок тестов.

Связи: тестирует `../../src/Dropwheel`. Общие правила — в корневом `LLM.md`.
