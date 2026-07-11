/**
 * Общий движок двухкольцевой отрисовки для мокапов overflow.
 *
 * Повторяет визуальный базис настоящего колеса из docs/demo/dropwheel.js:
 * те же палитры тем, хаб 56px с ядром-градиентом и болтами, скруглённые тайлы
 * 64px, спицы к радиусу r-52, подписи под тайлами. Отличие только одно —
 * геометрию раскладки задаёт страница-вариант через функцию layout(n, center),
 * которая возвращает кольца-ободы и слоты. Так каждый мокап отличается лишь
 * стратегией распределения тайлов по двум кольцам, а рисование общее.
 */

const OV = (() => {
  "use strict";

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
    globe: (ctx) => {
      ctx.strokeStyle = "#8fb8e6"; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.arc(0, 0, 12, 0, 7); ctx.stroke();
      ctx.beginPath(); ctx.ellipse(0, 0, 5, 12, 0, 0, 7); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(-12, 0); ctx.lineTo(12, 0);
      ctx.moveTo(-10, -6); ctx.lineTo(10, -6); ctx.moveTo(-10, 6); ctx.lineTo(10, 6); ctx.stroke();
    },
  };

  function captionColor(theme) {
    const m = theme.label.match(/\d+/g).map(Number);
    const lum = (0.299 * m[0] + 0.587 * m[1] + 0.114 * m[2]) / 255;
    return lum < 0.5 ? "#ECF1F7" : theme.label;
  }

  function accentA(theme, a) {
    const m = theme.accent.match(/\d+/g);
    return `rgba(${m[0]},${m[1]},${m[2]},${a})`;
  }

  /** Пул образцовых целей. Первые (n-1) идут на кольцо, последняя — тайл "+". */
  const POOL = [
    { label: "Downloads", icon: "download" },
    { label: "Documents", icon: "doc" },
    { label: "Desktop", icon: "desktop" },
    { label: "Pictures", icon: "pic" },
    { label: "Media", icon: "folder", group: true, num: "1" },
    { label: "Projects", icon: "folder" },
    { label: "Sort", icon: "folder", sorter: true },
    { label: "Music", icon: "folder" },
    { label: "Video", icon: "folder" },
    { label: "Work", icon: "folder" },
    { label: "Games", icon: "folder" },
    { label: "Backups", icon: "folder" },
    { label: "Cloud", icon: "globe" },
    { label: "Archive", icon: "folder" },
    { label: "Fonts", icon: "folder" },
    { label: "Notes", icon: "doc" },
  ];

  /** n тайлов: (n-1) целей из пула (циклом) + завершающий тайл "+". */
  function makeTiles(n) {
    const out = [];
    for (let i = 0; i < n - 1; i++) out.push(POOL[i % POOL.length]);
    out.push({ label: "", add: true });
    return out;
  }

  /** Углы count слотов на кольце радиуса r от стартового угла. Слот 0 — сверху. */
  function ring(count, r, start, cx, cy) {
    const out = [];
    for (let i = 0; i < count; i++) {
      const a = start + (i * 2 * Math.PI) / count;
      out.push({ a, x: cx + r * Math.cos(a), y: cy + r * Math.sin(a), r });
    }
    return out;
  }

  /**
   * Колесо на canvas. opts:
   *   size    — логический размер холста (окно приложения в реальном коде);
   *   theme   — имя темы;
   *   layout  — fn(n, center) → { rings:[{r,width}], slots:[{x,y,a,r,size,band}] };
   *   tiles   — массив тайлов (в порядке слотов);
   *   column  — подсвечивать всю радиальную колонну (совпадающий угол) при наведении.
   */
  class Wheel {
    constructor(canvas, opts) {
      this.canvas = canvas;
      this.ctx = canvas.getContext("2d");
      this.size = opts.size;
      this.center = this.size / 2;
      this.theme = THEMES[opts.theme] || THEMES.Dark;
      this.layoutFn = opts.layout;
      this.tiles = opts.tiles || makeTiles(11);
      this.column = !!opts.column;
      this.dpr = Math.min(window.devicePixelRatio || 1, 2);
      this.hover = -1;
      this._compute();
      this._fit();
      window.addEventListener("resize", () => this._fit());
      this._wire();
      this._loop = this._loop.bind(this);
      requestAnimationFrame(this._loop);
    }

    setTheme(name) { this.theme = THEMES[name] || this.theme; this._compute(); }
    setCount(n) { this.tiles = makeTiles(n); this._compute(); }

    _compute() {
      const L = this.layoutFn(this.tiles.length, this.center);
      this.rings = L.rings;
      this.slots = L.slots;
      this.caption = captionColor(this.theme);
    }

    _fit() {
      const w = this.canvas.clientWidth || this.size;
      this.canvas.width = Math.round(w * this.dpr);
      this.canvas.height = Math.round(w * this.dpr);
    }

    _wire() {
      const cv = this.canvas;
      cv.addEventListener("mousemove", (e) => {
        const r = cv.getBoundingClientRect();
        const mx = ((e.clientX - r.left) / r.width) * this.size;
        const my = ((e.clientY - r.top) / r.height) * this.size;
        let hit = -1;
        for (let i = 0; i < this.slots.length; i++) {
          const s = this.slots[i], h = s.size / 2;
          if (Math.abs(mx - s.x) < h && Math.abs(my - s.y) < h) { hit = i; break; }
        }
        this.hover = hit;
      });
      cv.addEventListener("mouseleave", () => { this.hover = -1; });
    }

    /** Совпадает ли угол слота с наведённым (для подсветки колонны). */
    _inHotColumn(i) {
      if (!this.column || this.hover < 0) return false;
      const ha = this.slots[this.hover].a;
      return Math.abs(((this.slots[i].a - ha + Math.PI) % (2 * Math.PI)) - Math.PI) < 0.001;
    }

    _loop() {
      this._draw();
      requestAnimationFrame(this._loop);
    }

    _draw() {
      const ctx = this.ctx, th = this.theme, C = this.center;
      const k = (this.canvas.width / this.dpr) / this.size;
      ctx.setTransform(this.dpr * k, 0, 0, this.dpr * k, 0, 0);
      ctx.clearRect(0, 0, this.size, this.size);

      for (const rg of this.rings) {
        ctx.strokeStyle = th.rim; ctx.lineWidth = rg.width;
        ctx.beginPath(); ctx.arc(C, C, rg.r, 0, 7); ctx.stroke();
      }

      for (let i = 0; i < this.slots.length; i++) {
        const s = this.slots[i];
        const lit = this.hover === i || this._inHotColumn(i);
        ctx.strokeStyle = lit ? th.accent : th.spoke;
        ctx.lineWidth = lit ? 2.5 : 2;
        ctx.beginPath();
        ctx.moveTo(C, C);
        ctx.lineTo(C + (s.r - 52) * Math.cos(s.a), C + (s.r - 52) * Math.sin(s.a));
        ctx.stroke();
      }

      this._drawHub(ctx);

      for (let i = 0; i < this.slots.length; i++) {
        const s = this.slots[i];
        const hot = this.hover === i || this._inHotColumn(i);
        this._drawTile(ctx, this.tiles[i], s.x, s.y, s.size, hot);
        if (this.tiles[i].label)
          this._drawCaption(ctx, this.tiles[i].label, s.x, s.y + s.size / 2 + 12);
      }
    }

    _drawHub(ctx) {
      const C = this.center;
      const haloR = 46;
      const halo = ctx.createRadialGradient(C, C, 2, C, C, haloR);
      halo.addColorStop(0, accentA(this.theme, 0.28));
      halo.addColorStop(1, accentA(this.theme, 0));
      ctx.fillStyle = halo;
      ctx.beginPath(); ctx.arc(C, C, haloR, 0, 7); ctx.fill();

      ctx.fillStyle = this.theme.hubBg;
      ctx.beginPath(); ctx.arc(C, C, 28, 0, 7); ctx.fill();
      ctx.strokeStyle = this.theme.hubBorder; ctx.lineWidth = 1; ctx.stroke();

      const core = ctx.createRadialGradient(C, C, 0, C, C, 11);
      core.addColorStop(0, this.theme.accent);
      core.addColorStop(1, this.theme.hubBg);
      ctx.fillStyle = core;
      ctx.beginPath(); ctx.arc(C, C, 11, 0, 7); ctx.fill();

      ctx.fillStyle = this.theme.hubBorder;
      for (const [bx, by] of [[0, -19], [0, 19], [-19, 0], [19, 0]]) {
        ctx.beginPath(); ctx.arc(C + bx, C + by, 2, 0, 7); ctx.fill();
      }
    }

    _drawTile(ctx, t, x, y, size, hot) {
      ctx.save();
      ctx.translate(x, y);
      const scale = (size / 64) * (hot ? 1.06 : 1);
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
      ctx.fillStyle = hot ? this.theme.tileHot : this.theme.tileBg;
      roundRect(ctx, -s, -s, s * 2, s * 2, 17); ctx.fill();
      ctx.shadowColor = "transparent"; ctx.shadowBlur = 0; ctx.shadowOffsetY = 0;

      ctx.lineWidth = 1.2;
      ctx.strokeStyle = t.group ? this.theme.groupBorder
        : t.sorter ? this.theme.sorterBorder : this.theme.tileBorder;
      roundRect(ctx, -s, -s, s * 2, s * 2, 17); ctx.stroke();

      if (t.group && t.num) {
        ctx.fillStyle = this.theme.label;
        ctx.font = "bold 22px system-ui,-apple-system,Segoe UI,sans-serif";
        ctx.textAlign = "center"; ctx.textBaseline = "middle";
        ctx.fillText(t.num, 0, 1);
      } else {
        (ICONS[t.icon] || ICONS.folder)(ctx);
      }

      if (t.sorter) {
        ctx.fillStyle = this.theme.sorterBorder;
        roundRect(ctx, s - 16, -s - 8, 22, 18, 9); ctx.fill();
        ctx.fillStyle = "#0c1420";
        ctx.font = "bold 12px system-ui,-apple-system,Segoe UI,sans-serif";
        ctx.textAlign = "center"; ctx.textBaseline = "middle";
        ctx.fillText("⇅", s - 5, -s + 1);
      }

      ctx.restore();
    }

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
  }

  return { Wheel, THEMES, makeTiles, ring };
})();
