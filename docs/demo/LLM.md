# docs/demo — заметка для нейросети

Роль папки: живая JS-документация колеса Dropwheel для GitHub Pages, замена GIF из
`docs/media`. Рисование на canvas, без сборки и зависимостей.

## Ключевые файлы

- `dropwheel.js` — движок. Модуль `DW` (IIFE) экспортирует класс `Wheel`, карту
  палитр `THEMES` (4 темы из `Themes.cs`) и константу `SIZE`. Внутри: геометрия
  (`CENTER=230`, `RING=170`, `HUB=56`, `SPOKE_R=RING-52`), сглаживания
  `backOut/cubicOut/sineOut`, параметры анимаций открытия `OPEN_ANIM`
  (pop/burst/sweep/settle), бейджи `BADGES` (copy/move/run/sorter/text/add/reorder),
  векторные иконки `ICONS`, отрисовка кадра в `Wheel._draw`.
- `scenes.js` — общий код всех сцен (наборы целей и таймлайны на
  `requestAnimationFrame`), одинаков для обеих языковых страниц. Локализуемая строка
  берётся из `window.DW_TXT`.
- `index.html` (RU) и `index.en.html` (EN) — тонкие страницы: только разметка и
  текст. Каждая перед `scenes.js` задаёт `window.DW_TXT` со своим переводом. Общие
  стили — `styles.css`. Наборы `id` в обеих страницах обязаны совпадать, иначе сцены
  не привяжутся.

## Правила именно здесь

- Числа геометрии, таймингов и цветов — зеркало настоящего кода. Источник правды:
  `src/Dropwheel/UI/OverlayWindow.Cloud.cs` (слоты, `AnimateTile`, `AnimateRim`),
  `OverlayWindow.Charge.cs` (проксимити-реакция), `OverlayWindow.PinDrop.cs` и
  `Capture.cs` (захват, кольцо-пульс), `Dnd.cs` (бейджи), `OverlayWindow.xaml`
  (размеры), `UI/Themes.cs` (палитры). Менять их тут можно только вслед за
  приложением.
- Анимация открытия по умолчанию — `Pop`: задержка `i*18мс`, длительность `220мс`,
  `BackEase` с амплитудой `0.36`.
- Иконки только векторные, нарисованные кодом. Никаких внешних картинок и
  системных иконок.

## Модель сцены

`DW.Wheel` рисует колесо; сцена каждый кадр выставляет управляемые поля и не
рисует сама. Поля: `forceHot`, `badges`, `ghost` (file/text/orb), `toast`,
`flash`, `chips`, `highlight`, `pinRing`, `cursor`, `orbPulse`/`orbLook`,
`orbOffset`/`orbAlpha`, `tileAngles`, `tileMul`, `orbBadge`. Закрытый орб —
`close()` (t0 в будущем: виден только хаб).

## Связи

Использует: ничего (чистый браузерный JS). Опирается по смыслу на UI-код в
`src/Dropwheel/UI`. Кто использует: страница GitHub Pages и ссылка «Live demo»
из корневого `README.md`.
