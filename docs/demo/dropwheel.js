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

  /** Палитра Dark-темы из Themes.cs (ARGB → css). */
  const THEME = {
    accent: "rgb(157,178,204)",
    tileBg: "rgba(32,38,48,.94)",
    tileHot: "rgba(46,56,72,.96)",
    tileBorder: "rgba(255,255,255,.22)",
    label: "rgb(201,210,222)",
    rim: "rgba(138,150,168,.10)",
    spoke: "rgba(192,200,212,.15)",
    hubBg: "rgba(26,32,42,.96)",
    hubBorder: "rgba(192,200,212,.30)",
    groupBorder: "rgba(138,176,255,.55)",
    sorterBorder: "rgba(232,166,72,.60)",
  };

  const clamp01 = (t) => (t < 0 ? 0 : t > 1 ? 1 : t);
  /** WPF BackEase easeOut с амплитудой amp. */
  const backOut = (t, amp) => {
    const u = 1 - t;
    return 1 - (u * u * u - u * amp * Math.sin(Math.PI * u));
  };
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
      // Программное состояние для сценариев (drop файла и т.п.):
      this.forceHot = -1;             // подсвеченный тайл помимо наведения
      this.badges = new Map();        // index → "copy" | "move"
      this.ghost = null;              // {x, y, label, mode, alpha} — призрак файла
      this.toast = null;              // {text, alpha}
      this.flash = null;              // {index, p} — вспышка при сбросе
      // startOpen: колесо уже раскрыто (для статичных сцен), иначе играем открытие
      this.t0 = opts.startOpen ? performance.now() - 4000 : performance.now();
      this._fit();
      window.addEventListener("resize", () => this._fit());
      if (this.hoverEnabled) this._wireHover();
      this._loop = this._loop.bind(this);
      requestAnimationFrame(this._loop);
    }

    open() { this.t0 = performance.now(); }

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
      const k = (this.canvas.width / this.dpr) / SIZE;
      ctx.setTransform(this.dpr * k, 0, 0, this.dpr * k, 0, 0);
      ctx.clearRect(0, 0, SIZE, SIZE);

      const n = this.tiles.length;

      // обод: opacity 200мс, scale .7→1 280мс, rotate −10°→0
      const rimP = cubicOut(clamp01(el / 280));
      const rimO = clamp01(el / 200);
      ctx.save();
      ctx.translate(CENTER, CENTER);
      ctx.rotate(((-10 + 10 * rimP) * Math.PI) / 180);
      ctx.scale(0.7 + 0.3 * rimP, 0.7 + 0.3 * rimP);
      ctx.globalAlpha = rimO;
      ctx.strokeStyle = THEME.rim; ctx.lineWidth = 34;
      ctx.beginPath(); ctx.arc(0, 0, RING, 0, 7); ctx.stroke();
      ctx.restore();

      // спицы (проявляются с ободом)
      ctx.globalAlpha = rimO;
      for (let i = 0; i < n; i++) {
        const s = slot(i, n);
        const lit = this.hoverEnabled && this.hover === i;
        ctx.strokeStyle = lit ? THEME.accent : THEME.spoke;
        ctx.lineWidth = lit ? 2.5 : 2;
        ctx.beginPath();
        ctx.moveTo(CENTER, CENTER);
        ctx.lineTo(CENTER + SPOKE_R * Math.cos(s.a), CENTER + SPOKE_R * Math.sin(s.a));
        ctx.stroke();
      }
      ctx.globalAlpha = 1;

      this._drawHub(ctx);

      // тайлы: Pop — задержка i*18, длительность 220 backOut(.36), opacity 140
      for (let i = 0; i < n; i++) {
        const s = slot(i, n);
        const t = this.tiles[i];
        const tt = el - i * 18;
        const p = backOut(clamp01(tt / 220), 0.36);
        const op = clamp01(tt / 140);
        const scale = 0.72 + 0.28 * p;
        const sx = -24 * Math.cos(s.a);
        const sy = -24 * Math.sin(s.a);
        const x = s.x + sx * (1 - p);
        const y = s.y - 8 + sy * (1 - p);
        const hot = (this.hoverEnabled && this.hover === i) || this.forceHot === i;
        ctx.globalAlpha = op;
        this._drawTile(ctx, t, x, y, scale * (hot ? 1.06 : 1), hot, this.badges.get(i));
        ctx.fillStyle = THEME.label;
        ctx.font = "11.5px system-ui,-apple-system,Segoe UI,sans-serif";
        ctx.textAlign = "center"; ctx.textBaseline = "middle";
        ctx.shadowColor = "rgba(0,0,0,.85)"; ctx.shadowBlur = 3; ctx.shadowOffsetY = 1;
        ctx.fillText(t.label, s.x, s.y + 40);
        ctx.shadowColor = "transparent"; ctx.shadowBlur = 0; ctx.shadowOffsetY = 0;
        ctx.globalAlpha = 1;
      }

      if (this.flash) this._drawFlash(ctx);
      if (this.ghost) this._drawGhost(ctx);
      if (this.toast) this._drawToast(ctx);
    }

    /** Вспышка-кольцо вокруг тайла в момент сброса. */
    _drawFlash(ctx) {
      const c = this.tileCenter(this.flash.index);
      const p = this.flash.p; // 0..1
      ctx.globalAlpha = (1 - p) * 0.9;
      ctx.strokeStyle = THEME.accent;
      ctx.lineWidth = 3;
      ctx.beginPath();
      ctx.arc(c.x, c.y, 34 + p * 22, 0, 7);
      ctx.stroke();
      ctx.globalAlpha = 1;
    }

    /** Призрак перетаскиваемого файла: карточка с уголком, подпись и бейдж курсора. */
    _drawGhost(ctx) {
      const g = this.ghost;
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
      const halo = ctx.createRadialGradient(CENTER, CENTER, 2, CENTER, CENTER, 46);
      halo.addColorStop(0, "rgba(157,178,204,.28)");
      halo.addColorStop(1, "rgba(157,178,204,0)");
      ctx.fillStyle = halo;
      ctx.beginPath(); ctx.arc(CENTER, CENTER, 46, 0, 7); ctx.fill();

      ctx.fillStyle = THEME.hubBg;
      ctx.beginPath(); ctx.arc(CENTER, CENTER, HUB / 2, 0, 7); ctx.fill();
      ctx.strokeStyle = THEME.hubBorder; ctx.lineWidth = 1; ctx.stroke();

      const core = ctx.createRadialGradient(CENTER, CENTER, 0, CENTER, CENTER, 11);
      core.addColorStop(0, THEME.accent);
      core.addColorStop(1, "rgba(26,32,42,.96)");
      ctx.fillStyle = core;
      ctx.beginPath(); ctx.arc(CENTER, CENTER, 11, 0, 7); ctx.fill();

      ctx.fillStyle = THEME.hubBorder;
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

      ctx.shadowColor = "rgba(0,0,0,.35)"; ctx.shadowBlur = 14; ctx.shadowOffsetY = 3;
      ctx.fillStyle = hot ? THEME.tileHot : THEME.tileBg;
      roundRect(ctx, -s, -s, s * 2, s * 2, 17); ctx.fill();
      ctx.shadowColor = "transparent"; ctx.shadowBlur = 0; ctx.shadowOffsetY = 0;

      ctx.lineWidth = 1.2;
      ctx.strokeStyle = t.group ? THEME.groupBorder : t.sorter ? THEME.sorterBorder : THEME.tileBorder;
      roundRect(ctx, -s, -s, s * 2, s * 2, 17); ctx.stroke();

      if (t.group) {
        if (t.num) {
          ctx.fillStyle = THEME.label;
          ctx.font = "bold 22px system-ui,-apple-system,Segoe UI,sans-serif";
          ctx.textAlign = "center"; ctx.textBaseline = "middle";
          ctx.fillText(t.num, 0, 1);
        }
      } else {
        (ICONS[t.icon] || ICONS.folder)(ctx);
      }

      if (t.code) {
        const w = Math.max(20, 10 + t.code.length * 7);
        ctx.fillStyle = THEME.accent;
        roundRect(ctx, -s - 2, -s - 6, w, 18, 9); ctx.fill();
        ctx.fillStyle = "#0d1017";
        ctx.font = "bold 11px Consolas,monospace";
        ctx.textAlign = "center"; ctx.textBaseline = "middle";
        ctx.fillText(t.code, -s - 2 + w / 2, -s - 6 + 9);
      }

      // бейдж копирования/перемещения (верхний правый угол тайла)
      if (badge) {
        ctx.fillStyle = "rgb(0,250,154)";
        roundRect(ctx, s - 16, -s - 8, 22, 18, 9); ctx.fill();
        ctx.fillStyle = "#06210f";
        ctx.font = "bold 13px system-ui,-apple-system,Segoe UI,sans-serif";
        ctx.textAlign = "center"; ctx.textBaseline = "middle";
        ctx.fillText(badge === "move" ? "➜" : "⧉", s - 5, -s + 1);
      }

      ctx.restore();
    }
  }

  return { Wheel, THEME, SIZE };
})();
