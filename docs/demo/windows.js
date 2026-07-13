/**
 * Галерея окон Dropwheel для демо-страницы.
 *
 * Рисует статичные, но точные по вёрстке копии оконного интерфейса приложения
 * (контекстное меню орба, окно новой группы, настройки, меню трея с журналом
 * недавних сбросов и тематический message box) и перекрашивает их всеми
 * четырьмя палитрами из src/Dropwheel/UI/Palette.cs. Разметка окон одна на обе
 * языковые страницы: содержимое окон — реальные английские строки приложения,
 * поэтому не локализуется; переводится только заголовок секции в самой странице.
 *
 * Точка входа — контейнер с id="windows-gallery" на странице. Скрипт наполняет
 * его переключателем тем и сеткой окон и вешает переключение палитры.
 */
(() => {
  "use strict";

  /** Палитры окон из Palette.cs (ARGB → css). titlebar — подложка строки
   * заголовка: у светлых тем чуть темнее фона окна, у тёмных чуть светлее. */
  const WPAL = {
    Fluent: { bg: "#F5F7FA", surface: "#FFFFFF", text: "#1B2430", muted: "#6B7686", border: "#D6DCE4", accent: "#2C7BE5", accentText: "#FFFFFF", danger: "#D13438", sel: "rgba(44,123,229,.094)", titlebar: "#EBEEF3", dark: false },
    Dark:   { bg: "#20262E", surface: "#2A313B", text: "#C9D2DE", muted: "#8A96A8", border: "#3A424E", accent: "#6FA8FF", accentText: "#0A0E14", danger: "#F0555C", sel: "rgba(111,168,255,.165)", titlebar: "#252C35", dark: true },
    Light:  { bg: "#F4F6F9", surface: "#FFFFFF", text: "#1A2430", muted: "#667080", border: "#D0D6DE", accent: "#0B62C6", accentText: "#FFFFFF", danger: "#C42B2B", sel: "rgba(11,98,198,.094)", titlebar: "#EAEEF3", dark: false },
    Neon:   { bg: "#061422", surface: "#0A1E30", text: "#CFEFF6", muted: "#6FA6B4", border: "#14384A", accent: "#35D6FF", accentText: "#04121A", danger: "#FF6B72", sel: "rgba(41,216,255,.188)", titlebar: "#081C2E", dark: true },
  };
  const THEME_ORDER = ["Fluent", "Dark", "Light", "Neon"];

  /** Иконка-точка приложения в строке заголовка (кружок акцентного цвета). */
  function appDot() {
    return '<span class="wdot"></span>';
  }

  /** Строка заголовка окна: точка приложения, название, кнопка закрытия. */
  function titlebar(title) {
    return '<div class="wtb">' + appDot() +
      '<span class="wttl">' + title + '</span>' +
      '<span class="wx">✕</span></div>';
  }

  /** Обёртка одного окна: строка заголовка плюс тело. width задаёт ширину карточки. */
  function win(title, body, width) {
    const w = width ? ' style="width:' + width + 'px"' : "";
    return '<div class="wwin"' + w + '>' + titlebar(title) + body + "</div>";
  }

  /** 1. Контекстное меню орба: создать группу / настройки. */
  function menuOrb() {
    const body =
      '<div class="wmenu wmenu-solo">' +
      '<div class="wmi">New group…</div>' +
      '<div class="wmi">Settings…</div>' +
      "</div>";
    return '<div class="wwin wwin-bare" style="width:230px">' + body + "</div>";
  }

  /** 2. Окно создания группы с inline-подсказкой и задизейбленной кнопкой Create. */
  function newGroup() {
    const body =
      '<div class="wbody">' +
      '<div class="wh">New group</div>' +
      '<div class="wlbl">Group name:</div>' +
      '<div class="winp"><span class="wcaret"></span></div>' +
      '<div class="whint">Enter a name</div>' +
      '<div class="wbtns">' +
      '<span class="wbtn">Cancel</span>' +
      '<span class="wbtn wdis">Create</span>' +
      "</div></div>";
    return win("New group", body, 300);
  }

  /** 3. Настройки: четыре раздела, левый список переключает правую панель. */
  function settings() {
    const nav =
      '<div class="wnav">' +
      '<div class="wnav-i on" data-sec="wheel">Wheel behavior</div>' +
      '<div class="wnav-i" data-sec="appearance">Appearance</div>' +
      '<div class="wnav-i" data-sec="hotkey">Hotkey &amp; shortcuts</div>' +
      '<div class="wnav-i" data-sec="system">System</div>' +
      "</div>";

    const wheel =
      '<div class="wpane on" data-sec="wheel">' +
      field("Default drop action", select("Copy")) +
      field("Hover delay to open, ms", input("250", 110)) +
      field("Wheel layout when a level has many targets", select("Overflow band — inner ring stays, extras go outside")) +
      field("Extra ring appears after this many targets", input("12", 110)) +
      '<label class="wcheck"><span class="wbox on">✓</span>Skip duplicate targets on the wheel</label>' +
      "</div>";

    const appearance =
      '<div class="wpane" data-sec="appearance">' +
      field("Theme", select("Fluent")) +
      field("Open animation", select("Pop")) +
      field("Animation speed", slider(33, "1.0×")) +
      field("Orb opacity", slider(100, "100%")) +
      field("Fade orb when idle, seconds (0 = off)", input("8", 110)) +
      "</div>";

    const hotkey =
      '<div class="wpane" data-sec="hotkey">' +
      '<div class="wfield"><div class="wflbl">Global hotkey</div>' +
      '<div class="whk-row"><div class="wnum wflex">Ctrl+Alt+Space</div>' +
      '<span class="wbtn wmini">Record</span><span class="wbtn wmini">Reset</span></div>' +
      '<div class="whk-status">Available</div></div>' +
      field("Common combinations", select("Default (Ctrl+Alt+Space)")) +
      field("Group shortcut sequence timeout, ms", input("600", 110)) +
      '<div class="wflbl wgst-h">Fixed orb gestures</div>' +
      '<div class="wgst"><span class="wgst-k">Alt + drag</span><span class="wgst-v">Move the orb</span></div>' +
      '<div class="wgst"><span class="wgst-k">Alt + Shift + drag</span><span class="wgst-v">Add a tile for the object under the cursor</span></div>' +
      "</div>";

    const system =
      '<div class="wpane" data-sec="system">' +
      '<label class="wcheck"><span class="wbox on">✓</span>Start with Windows</label>' +
      "</div>";

    const body =
      '<div class="wbody wbody-split">' + nav +
      '<div class="wpane-host">' + wheel + appearance + hotkey + system + "</div></div>" +
      '<div class="wbtns wbtns-bar">' +
      '<span class="wbtn">Cancel</span>' +
      '<span class="wbtn wpri">Save</span>' +
      "</div>";
    return win("Settings — Dropwheel", body, 470);
  }

  /** Псевдо-ползунок: заполнение и бегунок на позиции pct с подписью справа. */
  function slider(pct, label) {
    return '<div class="wsl"><div class="wsl-track">' +
      '<div class="wsl-fill" style="width:' + pct + '%"></div>' +
      '<div class="wsl-thumb" style="left:' + pct + '%"></div></div>' +
      '<span class="wsl-val">' + label + "</span></div>";
  }

  /** Поле настроек: подпись сверху, элемент управления снизу. */
  function field(label, control) {
    return '<div class="wfield"><div class="wflbl">' + label + "</div>" + control + "</div>";
  }

  /** Псевдо-выпадающий список с шевроном. */
  function select(value) {
    return '<div class="wselect"><span class="wsval">' + value + '</span><span class="wchev">⌄</span></div>';
  }

  /** Псевдо-поле ввода; ширина 0 — на всю доступную. */
  function input(value, width) {
    const w = width ? ' style="width:' + width + 'px"' : "";
    return '<div class="wnum"' + w + ">" + value + "</div>";
  }

  /** 4. Меню трея с раскрытым подменю недавних сбросов. */
  function tray() {
    const main =
      '<div class="wmenu">' +
      '<div class="wmi hdr">Dropwheel v0.16.3</div>' +
      '<div class="wmi chk"><span class="wtick">✓</span>Start with Windows</div>' +
      '<div class="wmi chk"><span class="wtick">✓</span>Explorer SendTo shortcut</div>' +
      '<div class="wmi">Settings…</div>' +
      '<div class="wmi">Open config folder</div>' +
      '<div class="wmi sub">Recent drops<span class="warr">›</span></div>' +
      '<div class="wmi">Exit</div>' +
      "</div>";
    const drops = [
      ["03:42", "Added 1 target → Wheel", false],
      ["03:37", "Added 1 target → Wheel", false],
      ["03:14", "Copied 1 file → Pictures", false],
      ["03:07", "Copied 1 file → Documents", false],
      ["03:07", "Telegram 1 file → DW-General", true],
      ["03:06", "Added 1 target → Wheel", false],
      ["02:38", "Telegram 1 file → DW-General", true],
    ];
    const rows = drops.map(([t, txt, mut]) =>
      '<div class="wmi drop' + (mut ? " muted" : "") + '"><span class="wtime">' + t + "</span>" + txt + "</div>").join("");
    const submenu =
      '<div class="wmenu wsubmenu">' + rows +
      '<div class="wsep"></div>' +
      '<div class="wmi">Clear history…</div>' +
      '<div class="wmi">Open history file…</div>' +
      "</div>";
    const body = '<div class="wtray">' + main + submenu + "</div>";
    return '<div class="wwin wwin-bare" style="width:430px">' + body + "</div>";
  }

  /** 5. Тематический message box на примере подтверждения запуска (trust gate). */
  function messageBox() {
    const body =
      '<div class="wbody">' +
      '<div class="wh">Run dropped files?</div>' +
      '<div class="wmsg">Dropwheel will run <b>Scripts</b> with 3 items as input.</div>' +
      '<div class="wbtns">' +
      '<span class="wbtn">Cancel</span>' +
      '<span class="wbtn wpri">Run</span>' +
      "</div></div>";
    return win("Run dropped files?", body, 340);
  }

  /** 6. Редактор цели в режиме сортера: базовые поля, список правил
   * (мастер), детали выбранного правила и предпросмотр раскладки. */
  function targetEditor() {
    const left =
      '<div class="wed-col-l">' +
      field("Name", input("Inbox", 0)) +
      field("Group shortcut (1–2 digits)", input("", 0)) +
      field("Path (folder or .exe)",
        '<div class="wpath"><div class="wnum wflex">D:\\Inbox</div><span class="wbtn wmini">…</span></div>') +
      field("Action on file drop", select("Default (global setting)")) +
      field("Group", select("(no group)")) +
      '<label class="wcheck"><span class="wbox"></span>Pin closer to center</label>' +
      "</div>";

    const rule = (n, dest, cond, sel) =>
      '<div class="wmr' + (sel ? " sel" : "") + '"><div class="wmr-n">' + n + ".  " + dest + "</div>" +
      '<div class="wmr-s">' + cond + "</div></div>";
    const master =
      '<div class="wed-col-m">' +
      '<div class="wmaster-h">Routing rules</div>' +
      '<div class="wmaster-sub">first match wins</div>' +
      rule("1", "Images", "ext is png jpg webp", true) +
      rule("2", "Docs", "ext is pdf docx", false) +
      rule("3", "(root)", "catch-all", false) +
      '<div class="wmaster-btns"><span class="wbtn wmini">+ Rule</span><span class="wbtn wmini">Presets ▾</span></div>' +
      '<label class="wcheck"><span class="wbox"></span>Watch folder, auto-sort new files</label>' +
      "</div>";

    const detail =
      '<div class="wed-col-d">' +
      '<div class="wd-head"><span class="wd-title">Rule 1</span>' +
      '<span class="wd-tools"><span class="wminibtn">▲</span><span class="wminibtn">▼</span><span class="wminibtn">✕</span></span></div>' +
      '<div class="wd-lbl">Destination (subfolder or absolute)</div>' +
      '<div class="wpath"><div class="wnum wflex">Images</div><span class="wbtn wmini">…</span></div>' +
      '<div class="wd-lbl">Conditions (all must match)</div>' +
      '<div class="wcond"><div class="wcond-top">' +
      '<div class="wselect wflex"><span class="wsval">Extension</span><span class="wchev">⌄</span></div>' +
      '<span class="wopword">is</span><span class="wminibtn">✕</span></div>' +
      '<div class="wnum">png jpg webp</div></div>' +
      '<div class="wed-btnrow"><span class="wbtn wmini">+ condition</span></div>' +
      '<div class="wed-btnrow"><span class="wbtn wmini">Save as preset…</span></div>' +
      "</div>";

    const preview =
      '<div class="wed-prev">' +
      '<div class="wed-prev-l">' +
      '<div class="wd-lbl wd-lbl-top">Test files (drag files here or one path per line)</div>' +
      '<div class="wtext">a.png\nb.pdf\nreport.jpg</div></div>' +
      '<div class="wed-prev-r">' +
      '<div class="wmaster-h">Preview</div>' +
      '<div class="wmaster-sub">Routes here: 2 of 3</div>' +
      '<div class="wprev-hit">a.png</div><div class="wprev-hit">report.jpg</div></div>' +
      "</div>";

    const body =
      '<div class="wbody wed">' +
      '<div class="wed-cols">' + left + master + detail + "</div>" +
      preview + "</div>" +
      '<div class="wbtns wbtns-bar wbtns-split">' +
      '<span class="wbtn wdanger">Delete target</span>' +
      '<span class="wed-foot-r"><span class="wbtn">Cancel</span><span class="wbtn wpri">Save</span></span>' +
      "</div>";
    return win("Target — Dropwheel", body, 940);
  }

  /** Применяет палитру темы к контейнеру галереи через CSS-переменные. */
  function applyTheme(gal, name) {
    const p = WPAL[name] || WPAL.Fluent;
    const set = (k, v) => gal.style.setProperty(k, v);
    set("--w-bg", p.bg); set("--w-surface", p.surface); set("--w-text", p.text);
    set("--w-muted", p.muted); set("--w-border", p.border); set("--w-accent", p.accent);
    set("--w-accent-text", p.accentText); set("--w-danger", p.danger); set("--w-sel", p.sel);
    set("--w-tb", p.titlebar);
    gal.dataset.wtheme = name;
  }

  /** Собирает галерею: переключатель тем и сетку окон, вешает переключение палитры. */
  function build(root) {
    const switcher =
      '<div class="wswitch" role="group" aria-label="Theme">' +
      THEME_ORDER.map((n, i) =>
        '<button type="button" class="wsw' + (i === 0 ? " on" : "") + '" data-th="' + n + '">' + n + "</button>").join("") +
      "</div>";
    const grid =
      '<div class="wgrid">' +
      menuOrb() + newGroup() + messageBox() + settings() + tray() + targetEditor() +
      "</div>";
    root.innerHTML = switcher + grid;

    const gal = root;
    applyTheme(gal, "Fluent");
    root.querySelector(".wswitch").addEventListener("click", (e) => {
      const b = e.target.closest("button[data-th]");
      if (!b) return;
      applyTheme(gal, b.dataset.th);
      for (const el of b.parentElement.children) el.classList.toggle("on", el === b);
    });

    root.addEventListener("click", (e) => {
      const nav = e.target.closest(".wnav-i[data-sec]");
      if (!nav) return;
      const host = nav.closest(".wbody-split");
      for (const it of host.querySelectorAll(".wnav-i")) it.classList.toggle("on", it === nav);
      for (const p of host.querySelectorAll(".wpane")) p.classList.toggle("on", p.dataset.sec === nav.dataset.sec);
    });
  }

  const root = document.getElementById("windows-gallery");
  if (root) build(root);
})();
