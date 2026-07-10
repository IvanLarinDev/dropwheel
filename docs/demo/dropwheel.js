/**
 * Движок отрисовки колеса Dropwheel на canvas.
 *
 * Повторяет геометрию и тайминги настоящего приложения (WPF): центр 230,230,
 * обод-кольцо радиусом 170 толщиной 34, хаб 56px с ядром-градиентом и четырьмя
 * болтами, спицы к радиусу 118, тайлы — скруглённые квадраты 64px. Анимация
 * открытия "Pop" повторяет OverlayWindow.Cloud.cs: вылет тайлов из центра с
 * задержкой 18мс на каждый и эффектом BackEase.
 *
 * Используется демо-страницей GitHub Pages вместо GIF-записей экрана.
 */

const DW = (() => {
  "use strict";

  const SIZE = 460;
  const CENTER = 230;
  const RING = 170;      // радиус центральной линии обода
  const HUB = 56;        // диаметр хаба
  const SPOKE_R = RING - 52;

  /** Четыре палитры из Themes.cs (ARGB → css). Поле stage — фон демо-подложки
   * под цвет темы (светлые темы над «светлым столом», тёмные — над тёмным). */
  const THEMES = {
    Fluent: {
      accent: "rgb(77,163,255)",
      tileBg: "rgba(255,255,255,.12)", tileHot: "rgba(255,255,255,.24)",
      tileBorder: "rgba(255,255,255,.24)", label: "rgb(219,231,245)",
      rim: "rgba(255,255,255,.08)", spoke: "rgba(255,255,255,.19)",
      hubBg: "rgba(255,255,255,.14)", hubBorder: "rgba(255,255,255,.30)",
      groupBorder: "rgba(124,196,255,.55)", sorterBorder: "rgba(255,184,77,.55)",
      stage: "#243447",
    },
    Dark: {
      accent: "rgb(157,178,204)",
      tileBg: "rgba(32,38,48,.94)", tileHot: "rgba(46,56,72,.96)",
      tileBorder: "rgba(255,255,255,.22)", label: "rgb(201,210,222)",
      rim: "rgba(138,150,168,.10)", spoke: "rgba(192,200,212,.15)",
      hubBg: "rgba(26,32,42,.96)", hubBorder: "rgba(192,200,212,.30)",
      groupBorder: "rgba(138,176,255,.55)", sorterBorder: "rgba(232,166,72,.60)",
      stage: "#0d1017",
    },
    Light: {
      accent: "rgb(11,98,198)",
      tileBg: "rgba(255,255,255,.91)", tileHot: "rgba(255,255,255,1)",
      tileBorder: "rgba(0,0,0,.19)", label: "rgb(26,36,48)",
      rim: "rgba(255,255,255,.19)", spoke: "rgba(0,0,0,.25)",
      hubBg: "rgba(255,255,255,.94)", hubBorder: "rgba(0,0,0,.25)",
      groupBorder: "rgba(61,125,214,.78)", sorterBorder: "rgba(216,138,30,.78)",
      labelBg: "rgba(255,255,255,.90)", stage: "#c3cbd6",
    },
    Neon: {
      accent: "rgb(89,245,255)",
      tileBg: "rgba(8,16,28,.90)", tileHot: "rgba(14,30,48,.90)",
      tileBorder: "rgba(41,216,255,.55)", label: "rgb(143,220,239)",
      rim: "rgba(41,216,255,.09)", spoke: "rgba(41,216,255,.20)",
      hubBg: "rgba(6,20,34,.90)", hubBorder: "rgba(41,216,255,.55)",
      groupBorder: "rgba(180,140,255,.61)", sorterBorder: "rgba(255,122,216,.61)",
      stage: "#061020",
    },
  };

  /** Параметры анимации открытия по OverlayWindow.Cloud.cs::AnimateTile. */
  const OPEN_ANIM = {
    pop:    { stagger: 18, dur: 220, opacity: 140, scale: 0.72, offMul: -24,  offMode: "radial",     ease: (t) => backOut(t, 0.36) },
    burst:  { stagger: 18, dur: 240, opacity: 140, scale: 0.46, offMul: -139, offMode: "radial",     ease: (t) => cubicOut(t) },
    sweep:  { stagger: 38, dur: 190, opacity: 120, scale: 0.68, offMul: 18,   offMode: "tangential", ease: (t) => sineOut(t) },
    settle: { stagger: 12, dur: 170, opacity: 140, scale: 0.86, offMul: -10,  offMode: "radial",     ease: (t) => backOut(t, 0.28) },
  };

  /** Бейджи действий на тайле (глиф + цвет) по OverlayWindow.Dnd.cs. */
  const BADGES = {
    copy:   { glyph: "⧉", color: "rgb(0,250,154)" },   // MediumSpringGreen
    move:   { glyph: "➜", color: "rgb(255,165,0)" },   // Orange
    run:    { glyph: "▶", color: "rgb(100,149,237)" }, // CornflowerBlue
    sorter: { glyph: "⇅", color: "rgb(232,166,72)" },  // amber (sorter)
    text:   { glyph: "≡", color: "rgb(0,250,154)" },   // text → save (copy)
    add:    { glyph: "+", color: "rgb(100,149,237)" }, // add as target
    reorder:{ glyph: "↕", color: "rgb(0,191,255)" },   // DeepSkyBlue
  };

  /** Цвет подписи под тайлом: светлый на тёмной подписи темы (как MakeLabel). */
  function captionColor(theme) {
    const m = theme.label.match(/\d+/g).map(Number);
    const lum = (0.299 * m[0] + 0.587 * m[1] + 0.114 * m[2]) / 255;
    return lum < 0.5 ? "#ECF1F7" : theme.label;
  }

  /** "rgb(r,g,b)" акцента темы → "rgba(r,g,b,a)". */
  function accentA(theme, a) {
    const m = theme.accent.match(/\d+/g);
    return `rgba(${m[0]},${m[1]},${m[2]},${a})`;
  }

  const clamp01 = (t) => (t < 0 ? 0 : t > 1 ? 1 : t);
  /** WPF BackEase easeOut с амплитудой amp. */
  const backOut = (t, amp) => {
    const u = 1 - t;
    return 1 - (u * u * u - u * amp * Math.sin(Math.PI * u));
  };
  /** WPF SineEase easeOut. */
  const sineOut = (t) => Math.sin((t * Math.PI) / 2);
  /** WPF CubicEase easeOut. */
  const cubicOut = (t) => {
    const u = 1 - t;
    return 1 - u * u * u;
  };

  /** Позиция слота i из n: слот 0 сверху (−90°), дальше по часовой. */
  function slot(i, n) {
    const a = -Math.PI / 2 + (i * 2 * Math.PI) / n;
    return { a, x: CENTER + RING * Math.cos(a), y: CENTER + RING * Math.sin(a) };
  }

  // ---------- иконки: рисуются в центре (0,0), бокс ~32px ----------
  function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r);
    ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
  }

  function folder(ctx, base, front) {
    ctx.fillStyle = base;
    roundRect(ctx, -14, -10.5, 12, 6, 2); ctx.fill();
    roundRect(ctx, -14, -8, 28, 17, 2.5); ctx.fill();
    ctx.fillStyle = front;
    roundRect(ctx, -14, -4, 28, 13, 2.5); ctx.fill();
  }

  const ICONS = {
    folder: (ctx) => folder(ctx, "#c98f2f", "#e9b24a"),
    desktop: (ctx) => folder(ctx, "#2f6bc0", "#4d8ce0"),
    doc: (ctx) => {
      ctx.fillStyle = "#f2f5fa";
      roundRect(ctx, -10, -13, 20, 26, 2.5); ctx.fill();
      ctx.fillStyle = "#cdd6e2";
      ctx.beginPath(); ctx.moveTo(4, -13); ctx.lineTo(10, -7); ctx.lineTo(4, -7); ctx.closePath(); ctx.fill();
      ctx.strokeStyle = "#9aa6b6"; ctx.lineWidth = 1.4;
      for (let i = 0; i < 4; i++) {
        ctx.beginPath(); ctx.moveTo(-6, -3 + i * 4.5); ctx.lineTo(6, -3 + i * 4.5); ctx.stroke();
      }
    },
    pic: (ctx) => {
      ctx.fillStyle = "#eef2f8";
      roundRect(ctx, -13, -11, 26, 22, 2.5); ctx.fill();
      ctx.fillStyle = "#f2c94c";
      ctx.beginPath(); ctx.arc(-5, -4, 3, 0, 7); ctx.fill();
      ctx.fillStyle = "#4f9d69";
      ctx.beginPath();
      ctx.moveTo(-13, 9); ctx.lineTo(-3, -1); ctx.lineTo(4, 6); ctx.lineTo(13, -2); ctx.lineTo(13, 9);
      ctx.closePath(); ctx.fill();
    },
    download: (ctx) => {
      ctx.fillStyle = "#2f8fe6";
      roundRect(ctx, -13, -13, 26, 26, 6); ctx.fill();
      ctx.strokeStyle = "#fff"; ctx.lineWidth = 3; ctx.lineCap = "round"; ctx.lineJoin = "round";
      ctx.beginPath(); ctx.moveTo(0, -7); ctx.lineTo(0, 5); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(-5, 0); ctx.lineTo(0, 5); ctx.lineTo(5, 0); ctx.stroke();
    },
    search: (ctx) => {
      ctx.strokeStyle = "#ef8a2a"; ctx.lineWidth = 4; ctx.lineCap = "round";
      ctx.beginPath(); ctx.arc(-3, -3, 7, 0, 7); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(2.5, 2.5); ctx.lineTo(10, 10); ctx.stroke();
      ctx.fillStyle = "rgba(255,255,255,.14)";
      ctx.beginPath(); ctx.arc(-3, -3, 5, 0, 7); ctx.fill();
    },
    gear: (ctx) => {
      ctx.fillStyle = "#b7c0cd";
      const teeth = 8;
      for (let i = 0; i < teeth; i++) {
        ctx.save(); ctx.rotate((i * Math.PI) / teeth * 2);
        roundRect(ctx, -2.5, -13, 5, 6, 1.5); ctx.fill(); ctx.restore();
      }
      ctx.beginPath(); ctx.arc(0, 0, 9, 0, 7); ctx.fill();
      ctx.fillStyle = "#0d1017";
      ctx.beginPath(); ctx.arc(0, 0, 3.5, 0, 7); ctx.fill();
    },
  };

  /**
   * Колесо на одном canvas. tiles — массив {label, icon, group, sorter, num,
   * code, add}. Метод open() запускает анимацию открытия заново.
   */
  class Wheel {
    constructor(canvas, tiles, opts = {}) {
      this.canvas = canvas;
      this.ctx = canvas.getContext("2d");
      this.tiles = tiles;
      this.hoverEnabled = opts.hover !== false;
      this.dpr = Math.min(window.devicePixelRatio || 1, 2);
      this.hover = -1;
      this.theme = THEMES[opts.theme] || THEMES.Dark;
      this.caption = captionColor(this.theme);
      this.animation = opts.animation || "pop";  // pop | burst | sweep | settle
      this.speed = opts.speed || 1;               // множитель скорости 0.5..2
      // Программное состояние для сценариев (drop файла и т.п.):
      this.forceHot = -1;             // подсвеченный тайл помимо наведения
      this.badges = new Map();        // index → "copy" | "move"
      this.ghost = null;              // {x, y, label, mode, alpha, kind} — призрак файла/текста
      this.toast = null;              // {text, alpha}
      this.flash = null;              // {index, p} — вспышка при сбросе
      this.orbPulse = 0;              // 0..1 — раздутие ореола хаба (проксимити)
      this.orbLook = { x: 0, y: 0 };  // смещение ядра «взглядом» к курсору
      this.cursor = null;             // {x, y, click} — указатель мыши в сцене
      // startOpen: колесо уже раскрыто (для статичных сцен), иначе играем открытие
      this.t0 = opts.startOpen ? performance.now() - 4000 : performance.now();
      this._fit();
      window.addEventListener("resize", () => this._fit());
      if (this.hoverEnabled) this._wireHover();
      this._loop = this._loop.bind(this);
      requestAnimationFrame(this._loop);
    }

    open() { this.t0 = performance.now(); }
    /** Свернуть колесо к одному хабу (обод и тайлы становятся невидимыми). */
    close() { this.t0 = performance.now() + 1e9; }

    setTheme(name) { this.theme = THEMES[name] || this.theme; this.caption = captionColor(this.theme); }
    setAnimation(name) { if (OPEN_ANIM[name]) this.animation = name; }
    setSpeed(v) { this.speed = Math.max(0.5, Math.min(2, v)); }

    /** Центр тайла i в логических координатах (460×460). */
    tileCenter(i) {
      const s = slot(i, this.tiles.length);
      return { x: s.x, y: s.y - 8 };
    }

    _fit() {
      const w = this.canvas.clientWidth || SIZE;
      this.canvas.width = Math.round(w * this.dpr);
      this.canvas.height = Math.round(w * this.dpr);
    }

    _wireHover() {
      const cv = this.canvas;
      cv.addEventListener("mousemove", (e) => {
        const r = cv.getBoundingClientRect();
        const mx = ((e.clientX - r.left) / r.width) * SIZE;
        const my = ((e.clientY - r.top) / r.height) * SIZE;
        const n = this.tiles.length;
        let hit = -1;
        for (let i = 0; i < n; i++) {
          const s = slot(i, n);
          if (Math.abs(mx - s.x) < 32 && Math.abs(my - (s.y - 8)) < 32) { hit = i; break; }
        }
        this.hover = hit;
      });
      cv.addEventListener("mouseleave", () => { this.hover = -1; });
    }

    _loop(now) {
      this._draw(now - this.t0);
      requestAnimationFrame(this._loop);
    }

    _draw(el) {
      const ctx = this.ctx;
      const th = this.theme;
      const sp = this.speed;
      const k = (this.canvas.width / this.dpr) / SIZE;
      ctx.setTransform(this.dpr * k, 0, 0, this.dpr * k, 0, 0);
      ctx.clearRect(0, 0, SIZE, SIZE);

      const n = this.tiles.length;

      // обод: opacity 200мс, scale .7→1 280мс, rotate −10°→0 (масштаб скорости)
      const rimP = cubicOut(clamp01(el / (280 / sp)));
      const rimO = clamp01(el / (200 / sp));
      ctx.save();
      ctx.translate(CENTER, CENTER);
      ctx.rotate(((-10 + 10 * rimP) * Math.PI) / 180);
      ctx.scale(0.7 + 0.3 * rimP, 0.7 + 0.3 * rimP);
      ctx.globalAlpha = rimO;
      ctx.strokeStyle = th.rim; ctx.lineWidth = 34;
      ctx.beginPath(); ctx.arc(0, 0, RING, 0, 7); ctx.stroke();
      ctx.restore();

      // спицы (проявляются с ободом)
      ctx.globalAlpha = rimO;
      for (let i = 0; i < n; i++) {
        const s = slot(i, n);
        const lit = (this.hoverEnabled && this.hover === i) || this.forceHot === i;
        ctx.strokeStyle = lit ? th.accent : th.spoke;
        ctx.lineWidth = lit ? 2.5 : 2;
        ctx.beginPath();
        ctx.moveTo(CENTER, CENTER);
        ctx.lineTo(CENTER + SPOKE_R * Math.cos(s.a), CENTER + SPOKE_R * Math.sin(s.a));
        ctx.stroke();
      }
      ctx.globalAlpha = 1;

      this._drawHub(ctx);

      // тайлы: параметры по выбранной анимации открытия, тайминги делятся на speed
      const A = OPEN_ANIM[this.animation] || OPEN_ANIM.pop;
      for (let i = 0; i < n; i++) {
        const s = slot(i, n);
        const t = this.tiles[i];
        const tt = el - (i * A.stagger) / sp;
        const p = A.ease(clamp01(tt / (A.dur / sp)));
        const op = clamp01(tt / (A.opacity / sp));
        const scale = A.scale + (1 - A.scale) * p;
        // радиальное или касательное стартовое смещение
        const off = A.offMode === "tangential"
          ? { x: A.offMul * Math.sin(s.a) * -1, y: A.offMul * Math.cos(s.a) }
          : { x: A.offMul * Math.cos(s.a), y: A.offMul * Math.sin(s.a) };
        const x = s.x + off.x * (1 - p);
        const y = s.y - 8 + off.y * (1 - p);
        const hot = (this.hoverEnabled && this.hover === i) || this.forceHot === i;
        ctx.globalAlpha = op;
        this._drawTile(ctx, t, x, y, scale * (hot ? 1.06 : 1), hot, this.badges.get(i));
        this._drawCaption(ctx, t.label, s.x, s.y + 40);
        ctx.globalAlpha = 1;
      }

      if (this.flash) this._drawFlash(ctx);
      if (this.ghost) this._drawGhost(ctx);
      if (this.toast) this._drawToast(ctx);
      if (this.cursor) this._drawCursor(ctx);
    }

    /** Подпись под тайлом: светлый текст с тенью, либо тёмный текст на светлой
     * подложке-пилюле (тема Light, у которой задан labelBg). */
    _drawCaption(ctx, text, x, y) {
      ctx.font = "11.5px system-ui,-apple-system,Segoe UI,sans-serif";
      ctx.textAlign = "center"; ctx.textBaseline = "middle";
      if (this.theme.labelBg) {
        const w = ctx.measureText(text).width + 12;
        ctx.fillStyle = this.theme.labelBg;
        roundRect(ctx, x - w / 2, y - 9, w, 18, 6); ctx.fill();
        ctx.fillStyle = this.theme.label;
        ctx.fillText(text, x, y);
        return;
      }
      ctx.fillStyle = this.caption;
      ctx.shadowColor = "rgba(0,0,0,.85)"; ctx.shadowBlur = 3; ctx.shadowOffsetY = 1;
      ctx.fillText(text, x, y);
      ctx.shadowColor = "transparent"; ctx.shadowBlur = 0; ctx.shadowOffsetY = 0;
    }

    /** Указатель мыши: стрелка с опциональным кольцом-кликом. */
    _drawCursor(ctx) {
      const cur = this.cursor;
      if (cur.click > 0) {
        ctx.globalAlpha = (1 - cur.click) * 0.7;
        ctx.strokeStyle = this.theme.accent; ctx.lineWidth = 2.5;
        ctx.beginPath(); ctx.arc(cur.x, cur.y, 6 + cur.click * 16, 0, 7); ctx.stroke();
        ctx.globalAlpha = 1;
      }
      ctx.save();
      ctx.translate(cur.x, cur.y);
      ctx.beginPath();
      ctx.moveTo(0, 0); ctx.lineTo(0, 17); ctx.lineTo(4.5, 12.5);
      ctx.lineTo(7.5, 18.5); ctx.lineTo(10, 17.3); ctx.lineTo(7, 11);
      ctx.lineTo(13, 11); ctx.closePath();
      ctx.fillStyle = "#f4f7fb"; ctx.fill();
      ctx.strokeStyle = "rgba(0,0,0,.65)"; ctx.lineWidth = 1.2; ctx.stroke();
      ctx.restore();
    }

    /** Вспышка-кольцо вокруг тайла в момент сброса. */
    _drawFlash(ctx) {
      const c = this.tileCenter(this.flash.index);
      const p = this.flash.p; // 0..1
      ctx.globalAlpha = (1 - p) * 0.9;
      ctx.strokeStyle = this.theme.accent;
      ctx.lineWidth = 3;
      ctx.beginPath();
      ctx.arc(c.x, c.y, 34 + p * 22, 0, 7);
      ctx.stroke();
      ctx.globalAlpha = 1;
    }

    /** Призрак перетаскиваемого файла (карточка) или выделенного текста (полоски
     * с подсветкой) — с подписью и бейджем режима у курсора. */
    _drawGhost(ctx) {
      const g = this.ghost;
      if (g.kind === "text") { this._drawTextGhost(ctx, g); return; }
      ctx.save();
      ctx.globalAlpha = g.alpha == null ? 1 : g.alpha;
      ctx.translate(g.x, g.y);
      ctx.rotate(-0.06);
      ctx.shadowColor = "rgba(0,0,0,.45)"; ctx.shadowBlur = 16; ctx.shadowOffsetY = 6;
      ctx.fillStyle = "#eef2f8";
      roundRect(ctx, -22, -28, 44, 56, 5); ctx.fill();
      ctx.shadowColor = "transparent"; ctx.shadowBlur = 0; ctx.shadowOffsetY = 0;
      // загнутый уголок
      ctx.fillStyle = "#c7d0dc";
      ctx.beginPath(); ctx.moveTo(10, -28); ctx.lineTo(22, -16); ctx.lineTo(10, -16); ctx.closePath(); ctx.fill();
      // строки текста
      ctx.strokeStyle = "#aab4c2"; ctx.lineWidth = 2;
      for (let i = 0; i < 4; i++) {
        ctx.beginPath(); ctx.moveTo(-14, -4 + i * 7); ctx.lineTo(14, -4 + i * 7); ctx.stroke();
      }
      ctx.rotate(0.06);
      // подпись файла под карточкой
      ctx.fillStyle = "#c9d2de"; ctx.font = "10px system-ui"; ctx.textAlign = "center"; ctx.textBaseline = "top";
      ctx.shadowColor = "rgba(0,0,0,.8)"; ctx.shadowBlur = 3;
      ctx.fillText(g.label || "report.pdf", 0, 32);
      ctx.shadowColor = "transparent"; ctx.shadowBlur = 0;
      // бейдж режима (copy/move) у курсора
      if (g.mode) {
        ctx.fillStyle = "rgb(0,250,154)";
        roundRect(ctx, 14, -30, 20, 18, 9); ctx.fill();
        ctx.fillStyle = "#06210f"; ctx.font = "bold 13px system-ui";
        ctx.textAlign = "center"; ctx.textBaseline = "middle";
        ctx.fillText(g.mode === "move" ? "➜" : "⧉", 24, -21);
      }
      ctx.restore();
    }

    /** Призрак выделенного текста: несколько строк с синей подсветкой, как
     * перетаскиваемое выделение из браузера или редактора. */
    _drawTextGhost(ctx, g) {
      ctx.save();
      ctx.globalAlpha = g.alpha == null ? 1 : g.alpha;
      ctx.translate(g.x, g.y);
      ctx.rotate(-0.05);
      const lines = [30, 44, 24, 40];
      ctx.shadowColor = "rgba(0,0,0,.4)"; ctx.shadowBlur = 12; ctx.shadowOffsetY = 4;
      ctx.fillStyle = "rgba(60,130,240,.28)";
      roundRect(ctx, -26, -22, 56, 44, 4); ctx.fill();
      ctx.shadowColor = "transparent"; ctx.shadowBlur = 0; ctx.shadowOffsetY = 0;
      for (let i = 0; i < lines.length; i++) {
        const y = -14 + i * 10;
        ctx.fillStyle = "rgba(90,150,245,.55)";
        roundRect(ctx, -22, y - 3, lines[i], 6, 2); ctx.fill();
      }
      ctx.rotate(0.05);
      ctx.fillStyle = "#c9d2de"; ctx.font = "10px system-ui";
      ctx.textAlign = "center"; ctx.textBaseline = "top";
      ctx.shadowColor = "rgba(0,0,0,.8)"; ctx.shadowBlur = 3;
      ctx.fillText(g.label || "выделенный текст", 0, 28);
      ctx.shadowColor = "transparent"; ctx.shadowBlur = 0;
      if (g.mode) {
        ctx.fillStyle = "rgb(0,250,154)";
        roundRect(ctx, 18, -26, 22, 18, 9); ctx.fill();
        ctx.fillStyle = "#06210f"; ctx.font = "bold 10px system-ui";
        ctx.textAlign = "center"; ctx.textBaseline = "middle";
        ctx.fillText("txt", 29, -17);
      }
      ctx.restore();
    }

    /** Тост внизу колеса: сообщение и ссылка Undo. */
    _drawToast(ctx) {
      const t = this.toast;
      ctx.globalAlpha = t.alpha == null ? 1 : t.alpha;
      ctx.font = "12px system-ui";
      const gap = "  ";
      const w = Math.min(300, ctx.measureText(t.text + gap + "Undo").width + 24);
      const x = CENTER - w / 2, y = 408, h = 30;
      ctx.fillStyle = "rgba(22,30,44,.92)";
      roundRect(ctx, x, y, w, h, 8); ctx.fill();
      ctx.textAlign = "left"; ctx.textBaseline = "middle";
      ctx.fillStyle = "#fff";
      ctx.fillText(t.text, x + 12, y + h / 2);
      ctx.fillStyle = "#7cc4ff"; ctx.font = "600 12px system-ui";
      ctx.fillText("Undo", x + 12 + ctx.measureText(t.text + gap).width, y + h / 2);
      ctx.globalAlpha = 1;
    }

    _drawHub(ctx) {
      // orbPulse (0..1) раздувает ореол при приближении drag'а; orbLook смещает
      // ядро «взглядом» в сторону курсора — как проксимити-реакция в приложении.
      const pulse = this.orbPulse || 0;
      const look = this.orbLook || { x: 0, y: 0 };
      const haloR = 46 + pulse * 16;
      const halo = ctx.createRadialGradient(CENTER, CENTER, 2, CENTER, CENTER, haloR);
      halo.addColorStop(0, accentA(this.theme, 0.28 + pulse * 0.22));
      halo.addColorStop(1, accentA(this.theme, 0));
      ctx.fillStyle = halo;
      ctx.beginPath(); ctx.arc(CENTER, CENTER, haloR, 0, 7); ctx.fill();

      ctx.fillStyle = this.theme.hubBg;
      ctx.beginPath(); ctx.arc(CENTER, CENTER, HUB / 2, 0, 7); ctx.fill();
      ctx.strokeStyle = this.theme.hubBorder; ctx.lineWidth = 1; ctx.stroke();

      const cx = CENTER + look.x, cy = CENTER + look.y;
      const core = ctx.createRadialGradient(cx, cy, 0, cx, cy, 11);
      core.addColorStop(0, this.theme.accent);
      core.addColorStop(1, this.theme.hubBg);
      ctx.fillStyle = core;
      ctx.beginPath(); ctx.arc(cx, cy, 11, 0, 7); ctx.fill();

      ctx.fillStyle = this.theme.hubBorder;
      for (const [bx, by] of [[0, -19], [0, 19], [-19, 0], [19, 0]]) {
        ctx.beginPath(); ctx.arc(CENTER + bx, CENTER + by, 2, 0, 7); ctx.fill();
      }
    }

    _drawTile(ctx, t, x, y, scale, hot, badge) {
      ctx.save();
      ctx.translate(x, y);
      ctx.scale(scale, scale);
      const s = 32;

      if (t.add) {
        ctx.setLineDash([6, 5]); ctx.lineWidth = 1.6;
        ctx.strokeStyle = "rgba(180,190,205,.45)";
        roundRect(ctx, -s, -s, s * 2, s * 2, 17); ctx.stroke();
        ctx.setLineDash([]);
        ctx.strokeStyle = "rgba(200,210,225,.7)"; ctx.lineWidth = 3; ctx.lineCap = "round";
        ctx.beginPath();
        ctx.moveTo(0, -11); ctx.lineTo(0, 11);
        ctx.moveTo(-11, 0); ctx.lineTo(11, 0);
        ctx.stroke();
        ctx.restore();
        return;
      }

      if (t.back) {
        ctx.shadowColor = "rgba(0,0,0,.35)"; ctx.shadowBlur = 14; ctx.shadowOffsetY = 3;
        ctx.fillStyle = hot ? this.theme.tileHot : this.theme.tileBg;
        roundRect(ctx, -s, -s, s * 2, s * 2, 17); ctx.fill();
        ctx.shadowColor = "transparent"; ctx.shadowBlur = 0; ctx.shadowOffsetY = 0;
        ctx.lineWidth = 1.2; ctx.strokeStyle = this.theme.tileBorder;
        roundRect(ctx, -s, -s, s * 2, s * 2, 17); ctx.stroke();
        // шеврон «назад»
        ctx.strokeStyle = this.theme.label; ctx.lineWidth = 3.2;
        ctx.lineCap = "round"; ctx.lineJoin = "round";
        ctx.beginPath();
        ctx.moveTo(5, -11); ctx.lineTo(-7, 0); ctx.lineTo(5, 11);
        ctx.stroke();
        ctx.restore();
        return;
      }

      ctx.shadowColor = "rgba(0,0,0,.35)"; ctx.shadowBlur = 14; ctx.shadowOffsetY = 3;
      ctx.fillStyle = hot ? this.theme.tileHot : this.theme.tileBg;
      roundRect(ctx, -s, -s, s * 2, s * 2, 17); ctx.fill();
      ctx.shadowColor = "transparent"; ctx.shadowBlur = 0; ctx.shadowOffsetY = 0;

      ctx.lineWidth = 1.2;
      ctx.strokeStyle = t.group ? this.theme.groupBorder : t.sorter ? this.theme.sorterBorder : this.theme.tileBorder;
      roundRect(ctx, -s, -s, s * 2, s * 2, 17); ctx.stroke();

      if (t.group) {
        if (t.num) {
          ctx.fillStyle = this.theme.label;
          ctx.font = "bold 22px system-ui,-apple-system,Segoe UI,sans-serif";
          ctx.textAlign = "center"; ctx.textBaseline = "middle";
          ctx.fillText(t.num, 0, 1);
        }
      } else {
        (ICONS[t.icon] || ICONS.folder)(ctx);
      }

      if (t.code) {
        const w = Math.max(20, 10 + t.code.length * 7);
        ctx.fillStyle = this.theme.accent;
        roundRect(ctx, -s - 2, -s - 6, w, 18, 9); ctx.fill();
        ctx.fillStyle = "#0d1017";
        ctx.font = "bold 11px Consolas,monospace";
        ctx.textAlign = "center"; ctx.textBaseline = "middle";
        ctx.fillText(t.code, -s - 2 + w / 2, -s - 6 + 9);
      }

      // бейдж действия (верхний правый угол тайла), цвет и глиф по OverlayWindow.Dnd.cs
      if (badge) {
        const b = BADGES[badge] || BADGES.copy;
        ctx.fillStyle = b.color;
        roundRect(ctx, s - 16, -s - 8, 22, 18, 9); ctx.fill();
        ctx.fillStyle = "#0c1420";
        ctx.font = "bold 13px system-ui,-apple-system,Segoe UI,sans-serif";
        ctx.textAlign = "center"; ctx.textBaseline = "middle";
        ctx.fillText(b.glyph, s - 5, -s + 1);
      }

      ctx.restore();
    }
  }

  return { Wheel, THEMES, SIZE };
})();
