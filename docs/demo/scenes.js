const SEL_TEXT = (window.DW_TXT && window.DW_TXT.selectedText) || "selected text";

    // Демонстрационный набор целей (собственный, не привязан к реальным папкам).
    const ROOT_TILES = [
      { label: "Downloads",  icon: "download" },
      { label: "Documents",  icon: "doc" },
      { label: "Pictures",   icon: "pic" },
      { label: "Desktop",    icon: "desktop" },
      { label: "Inbox",      icon: "folder", sorter: true },
      { label: "Projects",   icon: "group", group: true, num: "3", code: "1" },
      { label: "Scripts",    icon: "gear" },
      { label: "Archive",    icon: "folder" },
      { label: "Media",      icon: "group", group: true, num: "2", code: "2" },
      { label: "Everything", icon: "search" },
      { label: "Add",        icon: "plus", add: true },
    ];

    const stageOpen = document.getElementById("stage-open");
    const wheel = new DW.Wheel(document.getElementById("scene-open"), ROOT_TILES, { theme: "Dark" });
    stageOpen.style.background = DW.THEMES.Dark.stage;
    document.getElementById("replay-open").onclick = () => wheel.open();
    document.getElementById("anim-open").onchange = (e) => { wheel.setAnimation(e.target.value); wheel.open(); };
    document.getElementById("spd-open").oninput = (e) => {
      const v = parseFloat(e.target.value);
      wheel.setSpeed(v);
      document.getElementById("spd-val").textContent = v.toFixed(1) + "×";
      wheel.open();
    };
    document.getElementById("themes-open").addEventListener("click", (e) => {
      const b = e.target.closest("button[data-th]");
      if (!b) return;
      const name = b.dataset.th;
      wheel.setTheme(name);
      stageOpen.style.background = DW.THEMES[name].stage;
      for (const el of e.currentTarget.children) el.classList.toggle("on", el === b);
    });

    // ---- Сцена 2: перетаскивание файла (самоиграющаяся) ----
    const drop = new DW.Wheel(document.getElementById("scene-drop"), ROOT_TILES,
      { startOpen: true, hover: false });

    const lerp = (a, b, t) => a + (b - a) * t;
    const easeOut = (t) => 1 - (1 - t) * (1 - t) * (1 - t);
    const clamp01 = (t) => (t < 0 ? 0 : t > 1 ? 1 : t);

    // Два прохода: копирование на Downloads(0) и перемещение на Documents(1).
    const PASSES = [
      { index: 0, mode: "copy", label: "report.pdf", toast: "Copied to Downloads", activeLabel: "Copy to Downloads" },
      { index: 1, mode: "move", label: "notes.txt",  toast: "Moved to Documents", activeLabel: "Move to Documents" },
    ];
    const PASS_MS = 4200;
    const START = { x: 250, y: 505 };

    function runDropScene(now) {
      const total = now % (PASS_MS * PASSES.length);
      const pass = PASSES[Math.floor(total / PASS_MS)];
      const t = total % PASS_MS;
      const target = drop.tileCenter(pass.index);
      const lockPoint = { x: target.x - 30, y: target.y - 44 };

      // сброс состояния кадра
      drop.forceHot = -1;
      drop.badges.clear();
      drop.confidence = null;
      drop.ghost = null;
      drop.flash = null;
      drop.toast = null;

      if (t < 1300) {
        // подлёт файла к тайлу
        const p = easeOut(clamp01(t / 1000));
        drop.ghost = {
          x: lerp(START.x, lockPoint.x, p),
          y: lerp(START.y, lockPoint.y, p),
          mode: pass.mode, label: pass.label, alpha: 1,
        };
        if (t > 780) {
          drop.forceHot = pass.index;
          drop.confidence = {
            index: pass.index,
            mode: pass.mode,
            label: pass.mode === "move" ? "Move" : "Copy",
            activeLabel: pass.activeLabel,
          };
        }
      } else if (t < 1500) {
        // сброс: файл гаснет, тайл ещё горит
        const p = clamp01((t - 1300) / 200);
        drop.ghost = {
          x: lerp(lockPoint.x, target.x, p), y: lerp(lockPoint.y, target.y, p),
          mode: pass.mode, label: pass.label,
          alpha: 1 - (t - 1300) / 200,
        };
        drop.forceHot = pass.index;
        drop.confidence = {
          index: pass.index,
          mode: pass.mode,
          label: pass.mode === "move" ? "Move" : "Copy",
          activeLabel: pass.activeLabel,
        };
      }
      // кольцо-вспышка расходится от тайла после сброса
      if (t >= 1300 && t < 1800) {
        drop.flash = { index: pass.index, p: (t - 1300) / 500 };
      }

      // тост с отменой
      if (t >= 1400 && t < 3500) {
        const fadeIn = clamp01((t - 1400) / 150);
        const fadeOut = 1 - clamp01((t - 3300) / 200);
        drop.toast = { text: pass.toast, alpha: Math.min(fadeIn, fadeOut) };
      }

      requestAnimationFrame(runDropScene);
    }
    requestAnimationFrame(runDropScene);

    // ---- Сцена 3: открытие при приближении ----
    const prox = new DW.Wheel(document.getElementById("scene-prox"), ROOT_TILES, { hover: false });
    prox.close();
    const PROX_FAR = { x: 410, y: 455 }, PROX_NEAR = { x: 230, y: 262 };
    let px = { stage: 0, start: performance.now() };
    function proxScene(now) {
      const el = now - px.start;
      prox.ghost = null;
      if (px.stage === 0) {
        // файл подлетает к закрытому орбу
        const p = easeOut(clamp01(el / 1400));
        const gx = lerp(PROX_FAR.x, PROX_NEAR.x, p), gy = lerp(PROX_FAR.y, PROX_NEAR.y, p);
        prox.ghost = { x: gx, y: gy, mode: "copy", label: "photo.jpg", alpha: 1 };
        const d = Math.hypot(gx - 230, gy - 230);
        prox.orbPulse = clamp01((150 - d) / 110);
        const ux = d > 0 ? (gx - 230) / d : 0, uy = d > 0 ? (gy - 230) / d : 0;
        prox.orbLook = { x: ux * 4 * prox.orbPulse, y: uy * 4 * prox.orbPulse };
        if (el >= 1400) { prox.open(); px = { stage: 1, start: now }; }
      } else if (px.stage === 1) {
        // колесо раскрылось, ореол спадает
        prox.ghost = { x: PROX_NEAR.x, y: PROX_NEAR.y, mode: "copy", label: "photo.jpg", alpha: 1 };
        prox.orbPulse = Math.max(0, 1 - el / 500);
        prox.orbLook = { x: 0, y: 0 };
        if (el >= 1700) px = { stage: 2, start: now };
      } else if (px.stage === 2) {
        // файл отпущен и гаснет
        prox.ghost = { x: PROX_NEAR.x, y: PROX_NEAR.y, mode: "copy", label: "photo.jpg", alpha: Math.max(0, 1 - el / 300) };
        if (el >= 300) { prox.close(); px = { stage: 3, start: now }; }
      } else {
        // пауза, орб закрыт
        prox.orbPulse = 0;
        if (el >= 900) px = { stage: 0, start: now };
      }
      requestAnimationFrame(proxScene);
    }
    requestAnimationFrame(proxScene);

    // ---- Сцена 4: вход в группу ----
    const CHILD_TILES = [
      { label: "Back", back: true },
      { label: "web-app", icon: "folder" },
      { label: "api", icon: "folder" },
      { label: "docs", icon: "doc" },
      { label: "Add", icon: "plus", add: true },
    ];
    const group = new DW.Wheel(document.getElementById("scene-group"), ROOT_TILES, { startOpen: true, hover: false });
    let g = { stage: 0, start: performance.now(), cx: 360, cy: 360, click: 0, clicked: false };
    function groupScene(now) {
      const el = now - g.start;
      let desired = { x: 360, y: 360 }, hot = -1;
      if (g.stage === 0) {
        desired = group.tileCenter(5);                 // курсор к группе Projects
        if (el > 750) hot = 5;
        if (el > 950 && !g.clicked) { g.click = 0.001; g.clicked = true; }
        if (el >= 1250) { group.tiles = CHILD_TILES; group.open(); g = { ...g, stage: 1, start: now, clicked: false }; }
      } else if (g.stage === 1) {
        desired = group.tileCenter(0);                 // курсор к тайлу «Назад»
        if (el > 1100) hot = 0;
        if (el > 1300 && !g.clicked) { g.click = 0.001; g.clicked = true; }
        if (el >= 1650) { group.tiles = ROOT_TILES; group.open(); g = { ...g, stage: 2, start: now, clicked: false }; }
      } else {
        desired = { x: 360, y: 360 };                  // назад в исходную, пауза
        if (el >= 1400) g = { ...g, stage: 0, start: now, clicked: false };
      }
      g.cx += (desired.x - g.cx) * 0.14;
      g.cy += (desired.y - g.cy) * 0.14;
      if (g.click > 0) { g.click += 0.06; if (g.click >= 1) g.click = 0; }
      group.forceHot = hot;
      group.cursor = { x: g.cx, y: g.cy, click: g.click };
      requestAnimationFrame(groupScene);
    }
    requestAnimationFrame(groupScene);

    // ---- Сцена 5: сохранение выделенного текста ----
    const text = new DW.Wheel(document.getElementById("scene-text"), ROOT_TILES, { startOpen: true, hover: false });
    const TEXT_TARGET = 1, TEXT_START = { x: 250, y: 505 };
    const FILENAME = "text_2026-07-10_14-30-11.txt";
    function textScene(now) {
      const t = now % 5200;
      const tgt = text.tileCenter(TEXT_TARGET);
      text.forceHot = -1; text.badges.clear(); text.confidence = null; text.ghost = null; text.flash = null; text.toast = null;
      if (t < 1300) {
        const p = easeOut(clamp01(t / 1000));
        text.ghost = { kind: "text", x: lerp(TEXT_START.x, tgt.x, p), y: lerp(TEXT_START.y, tgt.y, p), mode: "txt", label: SEL_TEXT, alpha: 1 };
        if (t > 780) {
          text.forceHot = TEXT_TARGET;
          text.confidence = { index: TEXT_TARGET, mode: "text", label: "Text", activeLabel: "Save text in Documents" };
        }
      } else if (t < 1500) {
        text.ghost = { kind: "text", x: tgt.x, y: tgt.y, mode: "txt", label: SEL_TEXT, alpha: 1 - (t - 1300) / 200 };
        text.forceHot = TEXT_TARGET;
        text.confidence = { index: TEXT_TARGET, mode: "text", label: "Text", activeLabel: "Save text in Documents" };
      }
      if (t >= 1300 && t < 1800) text.flash = { index: TEXT_TARGET, p: (t - 1300) / 500 };
      if (t >= 1400 && t < 4600) {
        const fi = clamp01((t - 1400) / 150), fo = 1 - clamp01((t - 4400) / 200);
        text.toast = { text: "Saved " + FILENAME, alpha: Math.min(fi, fo) };
      }
      requestAnimationFrame(textScene);
    }
    requestAnimationFrame(textScene);

    // ---- Сцена 6: запуск (открыть с помощью) ----
    const run = new DW.Wheel(document.getElementById("scene-run"), ROOT_TILES, { startOpen: true, hover: false });
    const RUN_TARGET = 6; // Scripts
    function runScene(now) {
      const t = now % 4600;
      const tgt = run.tileCenter(RUN_TARGET);
      run.forceHot = -1; run.badges.clear(); run.confidence = null; run.ghost = null; run.flash = null; run.toast = null;
      if (t < 1300) {
        const p = easeOut(clamp01(t / 1000));
        run.ghost = { x: lerp(START.x, tgt.x, p), y: lerp(START.y, tgt.y, p), mode: "run", label: "data.csv", alpha: 1 };
        if (t > 780) {
          run.forceHot = RUN_TARGET;
          run.confidence = { index: RUN_TARGET, mode: "run", label: "Run", activeLabel: "Run with Scripts" };
        }
      } else if (t < 1500) {
        run.ghost = { x: tgt.x, y: tgt.y, mode: "run", label: "data.csv", alpha: 1 - (t - 1300) / 200 };
        run.forceHot = RUN_TARGET;
        run.confidence = { index: RUN_TARGET, mode: "run", label: "Run", activeLabel: "Run with Scripts" };
      }
      if (t >= 1300 && t < 1800) run.flash = { index: RUN_TARGET, p: (t - 1300) / 500 };
      if (t >= 1400 && t < 3800) {
        const fi = clamp01((t - 1400) / 150), fo = 1 - clamp01((t - 3600) / 200);
        run.toast = { text: "▶ Ran Scripts with data.csv", alpha: Math.min(fi, fo) };
      }
      requestAnimationFrame(runScene);
    }
    requestAnimationFrame(runScene);

    // ---- Сцена 7: сортер (раскладка по подпапкам) ----
    const sortW = new DW.Wheel(document.getElementById("scene-sort"), ROOT_TILES, { startOpen: true, hover: false });
    const SORT_TARGET = 4; // Inbox
    const SUBF = [
      { x: 140, y: 380, label: "Images/ 3", color: "rgba(232,166,72,.28)" },
      { x: 230, y: 380, label: "Docs/ 2",   color: "rgba(232,166,72,.28)" },
      { x: 320, y: 380, label: "Archive/ 1",color: "rgba(232,166,72,.28)" },
    ];
    const SORT_FILES = [
      { s: 0, l: "a.png" }, { s: 0, l: "b.jpg" }, { s: 0, l: "c.gif" },
      { s: 1, l: "d.pdf" }, { s: 1, l: "e.doc" }, { s: 2, l: "f.zip" },
    ];
    function sortScene(now) {
      const t = now % 5600;
      const tgt = sortW.tileCenter(SORT_TARGET);
      sortW.forceHot = -1; sortW.badges.clear(); sortW.confidence = null; sortW.ghost = null; sortW.chips = null; sortW.toast = null;
      if (t < 1200) {
        const p = easeOut(clamp01(t / 1000));
        sortW.ghost = { x: lerp(START.x, tgt.x, p), y: lerp(START.y, tgt.y, p), mode: "sorter", label: "6 files", alpha: 1 };
        if (t > 780) {
          sortW.forceHot = SORT_TARGET;
          sortW.confidence = { index: SORT_TARGET, mode: "sorter", label: "Sort", activeLabel: "Sort into Inbox" };
        }
      } else if (t < 4200) {
        sortW.forceHot = SORT_TARGET;
        sortW.confidence = { index: SORT_TARGET, mode: "sorter", label: "Sort", activeLabel: "Sort into Inbox" };
        const ft = t - 1200;
        const chips = SUBF.map((s) => ({ x: s.x, y: s.y, label: s.label, color: s.color }));
        for (let i = 0; i < SORT_FILES.length; i++) {
          const sub = SUBF[SORT_FILES[i].s];
          const p = easeOut(clamp01((ft - i * 170) / 620));
          if (p <= 0) continue;
          const alpha = Math.max(0, 1 - Math.max(0, ft - i * 170 - 620) / 300);
          chips.push({ x: lerp(tgt.x, sub.x, p), y: lerp(tgt.y, sub.y, p), label: SORT_FILES[i].l, color: "rgba(52,64,82,.96)", alpha, scale: 0.92 });
        }
        sortW.chips = chips;
        sortW.toast = { text: "⇅ Sorted: 6 items → Inbox", alpha: clamp01((ft - 200) / 200) };
      } else {
        sortW.chips = SUBF.map((s) => ({ x: s.x, y: s.y, label: s.label, color: s.color }));
        sortW.toast = { text: "⇅ Sorted: 6 items → Inbox", alpha: 1 - clamp01((t - 5000) / 300) };
      }
      requestAnimationFrame(sortScene);
    }
    requestAnimationFrame(sortScene);

    // ---- Сцена 8: захват цели орбом (Alt+Shift) ----
    const CAP_TILES = [
      { label: "report.pdf", icon: "doc" },   // захваченный, встаёт первым у хаба
      { label: "Downloads", icon: "download" },
      { label: "Documents", icon: "doc" },
      { label: "Pictures", icon: "pic" },
      { label: "Desktop", icon: "desktop" },
      { label: "Add", icon: "plus", add: true },
    ];
    const capture = new DW.Wheel(document.getElementById("scene-capture"), ROOT_TILES, { hover: false });
    capture.close();
    const HUB = { x: 230, y: 230 };
    const CAP_TARGET = { x: 296, y: 84, w: 100, h: 26, label: "report.pdf" };
    const capTcx = CAP_TARGET.x + CAP_TARGET.w / 2, capTcy = CAP_TARGET.y + CAP_TARGET.h / 2;
    let cap = { stage: 0, start: performance.now() };
    function captureScene(now) {
      const el = now - cap.start;
      capture.ghost = null; capture.highlight = null; capture.pinRing = null; capture.forceHot = -1; capture.toast = null;
      if (cap.stage === 0) {
        // призрак-орб летит от хаба к цели, «вооружается» и держит живую ауру
        const p = easeOut(clamp01(el / 1300));
        const arm = clamp01((el - 700) / 500);
        capture.ghost = { kind: "orb", x: lerp(HUB.x, capTcx, p), y: lerp(HUB.y, capTcy, p), arm, alpha: 0.92, ringT: el / 1000 };
        capture.highlight = { ...CAP_TARGET, alpha: clamp01((el - 600) / 300) };
        if (el >= 1900) cap = { stage: 1, start: now };
      } else if (cap.stage === 1) {
        // сматывание призрака в хаб + кольцо-пульс
        const p = easeOut(clamp01(el / 300));
        capture.ghost = { kind: "orb", x: lerp(capTcx, HUB.x, p), y: lerp(capTcy, HUB.y, p), arm: 1 - p, alpha: 0.92 * (1 - p), scale: 1 - 0.5 * p, ringT: (1900 + el) / 1000 };
        capture.highlight = { ...CAP_TARGET, alpha: 1 - clamp01(el / 200) };
        capture.pinRing = { p: clamp01(el / 300) };
        if (el >= 340) cap = { stage: 2, start: now };
      } else if (cap.stage === 2) {
        // короткая пауза-«бит», затем открываем колесо с закреплённым тайлом
        if (el >= 280) { capture.tiles = CAP_TILES; capture.open(); cap = { stage: 3, start: now }; }
      } else if (cap.stage === 3) {
        if (el < 600) capture.forceHot = 0;
        const fi = clamp01((el - 200) / 150), fo = 1 - clamp01((el - 2200) / 200);
        capture.toast = { text: "Pinned: report.pdf", alpha: Math.min(fi, fo) };
        if (el >= 2600) { capture.close(); cap = { stage: 4, start: now }; }
      } else {
        if (el >= 900) { capture.tiles = ROOT_TILES; cap = { stage: 0, start: now }; }
      }
      requestAnimationFrame(captureScene);
    }
    requestAnimationFrame(captureScene);

    // ---- Сцена 9: групповые коды (1 vs 11) ----
    const CODE_TILES = [
      { label: "Downloads", icon: "download" },
      { label: "Documents", icon: "doc" },
      { label: "Projects", icon: "group", group: true, num: "3", code: "1" },
      { label: "Pictures", icon: "pic" },
      { label: "Media", icon: "group", group: true, num: "2", code: "11" },
      { label: "Scripts", icon: "gear" },
      { label: "Archive", icon: "folder" },
      { label: "Everything", icon: "search" },
      { label: "Desktop", icon: "desktop" },
      { label: "Add", icon: "plus", add: true },
    ];
    const codes = new DW.Wheel(document.getElementById("scene-codes"), CODE_TILES, { startOpen: true, hover: false });
    function candidates(input) {
      const set = {};
      CODE_TILES.forEach((t, i) => { if (t.code && t.code.indexOf(input) === 0) set[i] = true; });
      return set;
    }
    function dimExcept(cand) {
      const mul = {};
      CODE_TILES.forEach((_, i) => { mul[i] = cand[i] ? 1 : 0.28; });
      return mul;
    }
    function codeScene(now) {
      const t = now % 4600;
      codes.forceHot = -1; codes.tileMul = null; codes.orbBadge = null; codes.flash = null;
      if (t >= 200 && t < 1200) {                 // набрана «1» — кандидаты 1 и 11
        codes.orbBadge = "1…"; codes.tileMul = dimExcept(candidates("1"));
      } else if (t >= 1200 && t < 1700) {         // тайм-аут вышел → открывается «1»
        codes.forceHot = 2; codes.flash = { index: 2, p: (t - 1200) / 500 };
      } else if (t >= 2300 && t < 2600) {         // снова «1…»
        codes.orbBadge = "1…"; codes.tileMul = dimExcept(candidates("1"));
      } else if (t >= 2600 && t < 3300) {         // быстрая вторая цифра → «11»
        codes.orbBadge = "11"; codes.tileMul = dimExcept(candidates("11"));
      } else if (t >= 3300 && t < 3800) {         // сразу открывается «11»
        codes.forceHot = 4; codes.flash = { index: 4, p: (t - 3300) / 500 };
      }
      requestAnimationFrame(codeScene);
    }
    requestAnimationFrame(codeScene);

    // ---- Сцена 10: перестановка тайлов по ободу ----
    const reorder = new DW.Wheel(document.getElementById("scene-reorder"), ROOT_TILES, { startOpen: true, hover: false });
    const RN = ROOT_TILES.length;               // 11 (Add — последний, не двигается)
    const baseA = (i) => -Math.PI / 2 + (i * 2 * Math.PI) / RN;
    const DRAG = 2;                              // тянем «Pictures»
    let curA = ROOT_TILES.map((_, i) => baseA(i));
    function reorderScene(now) {
      const frac = (now % 6000) / 6000;
      const s = 0.5 * (1 - Math.cos(frac * 2 * Math.PI)); // 0→1→0
      const insF = 2 + (7 - 2) * s;                       // непрерывная позиция вставки 2..7
      const insIdx = Math.max(0, Math.min(9, Math.round(insF)));
      const movable = [];
      for (let i = 0; i < 10; i++) if (i !== DRAG) movable.push(i);
      movable.splice(insIdx, 0, DRAG);
      const target = new Array(RN);
      for (let pos = 0; pos < movable.length; pos++) target[movable[pos]] = baseA(pos);
      target[10] = baseA(10);                    // Add фиксирован
      target[DRAG] = baseA(insF);                // тянущийся тайл — по непрерывной позиции
      for (let i = 0; i < RN; i++) {
        if (i === DRAG) curA[i] = target[i];
        else curA[i] += (target[i] - curA[i]) * 0.2;
      }
      reorder.tileAngles = curA.slice();
      reorder.forceHot = DRAG;
      reorder.badges.clear(); reorder.badges.set(DRAG, "reorder");
      const a = baseA(insF);
      reorder.cursor = { x: 230 + 170 * Math.cos(a), y: 230 + 170 * Math.sin(a) - 8, click: 0 };
      requestAnimationFrame(reorderScene);
    }
    requestAnimationFrame(reorderScene);

    // ---- Сцена 11: ссылка → заголовок и фавикон ----
    const LINK_BASE = [
      { label: "Downloads", icon: "download" }, { label: "Documents", icon: "doc" },
      { label: "Pictures", icon: "pic" }, { label: "Desktop", icon: "desktop" },
      { label: "Everything", icon: "search" }, { label: "Add", icon: "plus", add: true },
    ];
    const link = new DW.Wheel(document.getElementById("scene-link"), LINK_BASE.slice(), { startOpen: true, hover: false });
    let lk = { stage: 0, start: performance.now() };
    function linkReset() { link.tiles = LINK_BASE.slice(); }
    function linkScene(now) {
      const el = now - lk.start;
      const addIdx = link.tiles.length - 1;
      link.ghost = null; link.forceHot = -1; link.badges.clear(); link.toast = null; link.flash = null;
      if (lk.stage === 0) {
        // ссылка-призрак летит на «+»
        const tgt = link.tileCenter(addIdx);
        const p = easeOut(clamp01(el / 1000));
        link.ghost = { x: lerp(START.x, tgt.x, p), y: lerp(START.y, tgt.y, p), mode: "add", label: "example.com", alpha: 1 };
        if (el > 780) { link.forceHot = addIdx; link.badges.set(addIdx, "add"); }
        if (el >= 1250) {
          // вставляем новый тайл перед «+», проигрываем прибытие
          const t = link.tiles.slice();
          t.splice(t.length - 1, 0, { label: "example.com", icon: "globe" });
          link.tiles = t; link.open();
          lk = { stage: 1, start: now, tileIdx: t.length - 2 };
        }
      } else if (lk.stage === 1) {
        // тайл появился; через паузу приходят метаданные
        link.forceHot = lk.tileIdx;
        if (el >= 900) {
          const nt = link.tiles[lk.tileIdx];
          nt.label = "Example Domain"; nt.icon = null; nt.fav = { color: "#3b6fd4", letter: "E" };
          link.flash = { index: lk.tileIdx, p: 0 };
          lk = { stage: 2, start: now, tileIdx: lk.tileIdx };
        }
      } else if (lk.stage === 2) {
        link.forceHot = lk.tileIdx;
        if (el < 500) link.flash = { index: lk.tileIdx, p: el / 500 };
        if (el >= 2200) { linkReset(); lk = { stage: 3, start: now }; }
      } else {
        if (el >= 900) lk = { stage: 0, start: now };
      }
      requestAnimationFrame(linkScene);
    }
    requestAnimationFrame(linkScene);

    // ---- Сцена 12: отмена ----
    const undo = new DW.Wheel(document.getElementById("scene-undo"), ROOT_TILES, { startOpen: true, hover: false });
    const UNDO_TARGET = 1; // Documents
    let ud = { stage: 0, start: performance.now(), cx: 250, cy: 505, click: 0, clicked: false };
    function undoScene(now) {
      const el = now - ud.start;
      const tgt = undo.tileCenter(UNDO_TARGET);
      undo.forceHot = -1; undo.badges.clear(); undo.ghost = null; undo.flash = null; undo.toast = null; undo.cursor = null;
      if (ud.stage === 0) {                 // перемещение файла на Documents
        const p = easeOut(clamp01(el / 1000));
        undo.ghost = { x: lerp(250, tgt.x, p), y: lerp(505, tgt.y, p), mode: "move", label: "notes.txt", alpha: el < 1100 ? 1 : Math.max(0, 1 - (el - 1100) / 200) };
        if (el > 780) { undo.forceHot = UNDO_TARGET; undo.badges.set(UNDO_TARGET, "move"); }
        if (el >= 1100 && el < 1600) undo.flash = { index: UNDO_TARGET, p: (el - 1100) / 500 };
        undo.toast = { text: "➜ Moved: 1 item → Documents", alpha: clamp01((el - 1200) / 200) };
        ud.cx += (300 - ud.cx) * 0.12; ud.cy += (423 - ud.cy) * 0.12; // курсор к ссылке Undo
        undo.cursor = { x: ud.cx, y: ud.cy, click: 0 };
        if (el >= 2200) ud = { ...ud, stage: 1, start: now, clicked: false };
      } else if (ud.stage === 1) {          // клик по «Undo»
        undo.toast = { text: "➜ Moved: 1 item → Documents", alpha: 1 };
        undo.forceHot = UNDO_TARGET;
        if (!ud.clicked) { ud.click = 0.001; ud.clicked = true; }
        if (ud.click > 0) { ud.click += 0.06; if (ud.click >= 1) ud.click = 0; }
        undo.cursor = { x: 300, y: 423, click: ud.click };
        if (el >= 500) ud = { ...ud, stage: 2, start: now };
      } else if (ud.stage === 2) {          // файл возвращается, тост «Undone»
        const p = easeOut(clamp01(el / 700));
        undo.ghost = { x: lerp(tgt.x, 250, p), y: lerp(tgt.y, 505, p), mode: "move", label: "notes.txt", alpha: Math.max(0, 1 - p * 0.8) };
        undo.toast = { text: "↩ Undone", alpha: 1 - clamp01((el - 1200) / 300) };
        if (el >= 1600) ud = { stage: 3, start: now, cx: 250, cy: 505, click: 0, clicked: false };
      } else {
        if (el >= 700) ud = { stage: 0, start: now, cx: 250, cy: 505, click: 0, clicked: false };
      }
      requestAnimationFrame(undoScene);
    }
    requestAnimationFrame(undoScene);

    // ---- Сцена 13: поведение орба (перемещение + затухание) ----
    const orb = new DW.Wheel(document.getElementById("scene-orb"), ROOT_TILES, { hover: false });
    orb.close();
    function orbScene(now) {
      const t = now % 6000;
      orb.cursor = null;
      if (t < 1600) {
        // Alt+перетаскивание: орб едет слева направо
        const p = easeOut(clamp01(t / 1600));
        orb.orbOffset = { x: lerp(-110, 110, p), y: lerp(40, -30, p) };
        orb.orbAlpha = 1;
      } else if (t < 2600) {
        orb.orbOffset = { x: 110, y: -30 }; orb.orbAlpha = 1;      // осел на месте
      } else if (t < 3600) {
        orb.orbAlpha = 1 - 0.72 * clamp01((t - 2600) / 900);      // затухание при простое
      } else if (t < 4400) {
        orb.orbAlpha = 0.28;                                       // потускневший орб
      } else {
        // курсор приближается — орб просыпается
        const p = clamp01((t - 4400) / 900);
        orb.orbAlpha = 0.28 + 0.72 * p;
        orb.cursor = { x: lerp(360, 300, p), y: lerp(360, 210, p), click: 0 };
      }
      requestAnimationFrame(orbScene);
    }
    requestAnimationFrame(orbScene);

    // ---- Глоссарий: колесо с пронумерованными маркерами частей интерфейса ----
    const gloss = new DW.Wheel(document.getElementById("scene-gloss"), ROOT_TILES, { startOpen: true, hover: false });
    gloss.badges.set(1, "copy"); // показать бейдж на Documents для пункта 7
    const anno = document.getElementById("gloss-anno");
    const actx = anno.getContext("2d");
    function drawAnno() {
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      const w = anno.clientWidth || 460;
      anno.width = Math.round(w * dpr); anno.height = Math.round(w * dpr);
      const k = (anno.width / dpr) / 460;
      actx.setTransform(dpr * k, 0, 0, dpr * k, 0, 0);
      actx.clearRect(0, 0, 460, 460);
      const NN = ROOT_TILES.length;
      const A = (i) => -Math.PI / 2 + (i * 2 * Math.PI) / NN;
      const tc = (i) => ({ x: 230 + 170 * Math.cos(A(i)), y: 230 + 170 * Math.sin(A(i)) - 8 });
      const midA = A(0) + (Math.PI / NN);
      const pts = [
        { n: 1, x: 230, y: 230 },                                                 // хаб
        { n: 2, x: 230 + 170 * Math.cos(midA), y: 230 + 170 * Math.sin(midA) },   // обод
        { n: 3, x: 230 + 59 * Math.cos(A(8)), y: 230 + 59 * Math.sin(A(8)) },     // спицы
        { n: 4, x: tc(0).x, y: tc(0).y },                                         // тайл-цель
        { n: 5, x: 230, y: 230 + 170 * Math.sin(A(0)) + 40 },                     // подпись
        { n: 6, x: tc(10).x, y: tc(10).y },                                       // Add
        { n: 7, x: tc(1).x + 22, y: tc(1).y - 22 },                               // бейдж
        { n: 8, x: tc(5).x, y: tc(5).y },                                         // группа
        { n: 9, x: tc(4).x, y: tc(4).y },                                         // сортер
      ];
      for (const p of pts) {
        actx.beginPath(); actx.arc(p.x, p.y, 11, 0, 7);
        actx.fillStyle = "rgba(18,26,40,.92)"; actx.fill();
        actx.strokeStyle = "#7cc4ff"; actx.lineWidth = 1.5; actx.stroke();
        actx.fillStyle = "#cfe6ff"; actx.font = "bold 12px system-ui,-apple-system,Segoe UI,sans-serif";
        actx.textAlign = "center"; actx.textBaseline = "middle";
        actx.fillText(String(p.n), p.x, p.y + 0.5);
      }
    }
    drawAnno();
    window.addEventListener("resize", drawAnno);
    // перерисовать метки после проигрывания открытия (тайлы встают на места)
    setTimeout(drawAnno, 900);
