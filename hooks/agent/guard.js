#!/usr/bin/env node
// guard.js — единый agent-adapter хук (PreToolUse на Bash/Write/Edit/Read и т.п.).
//
// Архитектура (паттерн ECC bash-hook-dispatcher): вся логика — в экспортируемой
// синхронной run(ctx, env) -> { exitCode, stdout, stderr }, БЕЗ process.exit и
// побочных эффектов вывода. CLI-обёртка внизу читает stdin и применяет результат.
// Это даёт in-process запуск из тестов/диспетчеров без ~50-100мс спавна на вызов.
//
// БЛОК (exit 2):
//   • обход харнесса: --no-verify / git commit -n, core.hooksPath (config и -c),
//     LEFTHOOK=0, lefthook uninstall, запись в .git/hooks;
//   • правка файлов харнесса (hooks/, lefthook.yml, конфиги, workflows) — file-tools
//     И shell (POSIX rm/mv/sed -i/tee/редирект + cmd/PowerShell del/move/Remove-Item/
//     Set-Content…); пути нормализуются, разделитель / и \;
//   • правка СУЩЕСТВУЮЩЕГО lint/format-конфига проекта (создание нового — можно);
//   • дегенеративные циклы; мусор tool-разметки; низкоэнтропийная команда;
//   • обрезанный/нечитаемый входной payload (fail-closed, всегда включён).
// NOTE (exit 0 + additionalContext):
//   • git commit/merge/push или правка файлов на main/master;
//   • правка UI-файла — напоминание о DESIGN-стадии;
//   • fact-force (паттерн ECC GateGuard): правка существующего файла, который
//     в этой сессии ни разу не читали — EXPLORE прежде IMPLEMENT (1 note на файл).
//
// Профили строгости (для ЧЕЛОВЕКА; env задаёт раннер, не команды агента):
//   HARNESS_PROFILE=minimal   — только анти-обход + защита файлов харнесса;
//   HARNESS_PROFILE=standard  — всё (default);
//   HARNESS_PROFILE=strict    — всё + пороги циклов вдвое ниже.
//   HARNESS_DISABLED_CHECKS=loops,entropy — точечное отключение проверок.
// Escape одобренного обхода в block-сообщениях агенту не называется (см. AGENTS.md → Env).
// Ошибка самого хука никогда не блокирует работу (fail-open → exit 0).

const fs = require("fs");
const os = require("os");
const path = require("path");
const crypto = require("crypto");
const { execSync } = require("child_process");
const { parse } = require(path.join(__dirname, "_input.js"));
const { globToRe, loadConfig, normRel, isProtectedPath, isProtectedShellWrite,
  isLintConfigShellWrite, isLintConfigPath, interpreterProtectedHint } = require(path.join(__dirname, "..", "_lib.js"));

const TTL_MS = 2 * 60 * 60 * 1000;
const SEEN_MAX = 200;
const BYPASS_HINT = "Обход возможен только по явному одобрению пользователя — попроси его (escape описан в AGENTS.md → Env).";

// ---------- профили ----------
const ALL_CHECKS = ["bypass", "protected", "lintconfig", "corruption", "entropy", "loops", "main-note", "design-note", "fact-force"];
const PROFILES = {
  minimal: new Set(["bypass", "protected"]),
  standard: new Set(ALL_CHECKS),
  strict: new Set(ALL_CHECKS),
};
function getProfile(env) {
  const p = String(env.HARNESS_PROFILE || "standard").trim().toLowerCase();
  return PROFILES[p] ? p : "standard";
}
function checkEnabled(id, env) {
  const off = String(env.HARNESS_DISABLED_CHECKS || "").split(",").map((s) => s.trim().toLowerCase());
  if (off.includes(id)) return false;
  return PROFILES[getProfile(env)].has(id);
}
function envAllow(env, name) {
  return ["1", "true", "yes", "on"].includes(String(env[name] || "").trim().toLowerCase());
}

// ---------- результат (вместо process.exit / stdout из логики) ----------
function allowRes(notes) {
  if (!notes || !notes.length) return { exitCode: 0, stdout: "", stderr: "" };
  const text = notes.join("\n");
  return {
    exitCode: 0,
    stdout: JSON.stringify({
      additionalContext: text, // простые раннеры
      hookSpecificOutput: { hookEventName: "PreToolUse", additionalContext: text }, // Claude Code
    }) + "\n",
    stderr: text + "\n",
  };
}
function blockRes(text) {
  return { exitCode: 2, stdout: "", stderr: text + "\n" };
}

// ---------- state (per session, в tmpdir) ----------
function stateFile(sessionId, projectDir) {
  const id = sessionId || "proj-" + crypto.createHash("sha1").update(String(projectDir)).digest("hex").slice(0, 12);
  return path.join(os.tmpdir(), `harness-guard-${id}.json`);
}
function readState(p) {
  try {
    const s = JSON.parse(fs.readFileSync(p, "utf8"));
    if (Date.now() - s.ts > TTL_MS) return { hist: [], streak: 0, seen: [] };
    return { hist: s.hist || [], streak: s.streak || 0, seen: s.seen || [] };
  } catch { return { hist: [], streak: 0, seen: [] }; }
}
// Атомарная запись: temp-файл в том же каталоге (tmpdir) + rename. Иначе
// параллельные PreToolUse-хуки могли оставить оборванный JSON или затереть
// историю на полузаписи (гонка read-modify-write). rename атомарен в пределах ФС.
function writeState(p, s) {
  const tmp = p + "." + process.pid + ".tmp";
  try {
    fs.writeFileSync(tmp, JSON.stringify({ ...s, ts: Date.now() }));
    fs.renameSync(tmp, p);
  } catch { try { fs.unlinkSync(tmp); } catch {} }
}
function markSeen(st, rel) {
  if (!st.seen.includes(rel)) {
    st.seen.push(rel);
    if (st.seen.length > SEEN_MAX) st.seen.shift();
  }
}

function currentBranch(cwd) {
  try {
    const b = execSync("git symbolic-ref --short HEAD", { cwd, stdio: ["ignore", "pipe", "ignore"], timeout: 2000, killSignal: "SIGKILL" }).toString().trim();
    return b === "HEAD" ? "" : b;
  } catch { return ""; }
}

// ---------- детекторы ----------
// Узкий паттерн разметки tool-call'ов: <tool_call>, </tool_use>, <function_call…
// (НЕ любой "<tool…" — heredoc с <toolbar> это легитимная запись HTML).
const CORRUPTION_RE = /<\/?(?:tool_?(?:call|use|result)|function_call|invoke|antml)[\s>_:/]|^\s*["']\s*,?\s*\d*\s*</i;
function isTrivial(cmd) {
  const c = cmd.trim();
  if (!c) return true;
  if (/^echo\s+(['"]?)([^\s|;&"'{}]{1,8})\1$/.test(c)) return true;
  if (/^ls(\s+-{1,2}[a-zA-Z-]+)*\s*$/.test(c)) return true;
  if (/^(pwd|true|false|clear|date|:)\s*$/.test(c)) return true;
  return false;
}
function isLowEntropy(cmd) {
  const t = cmd.trim().split(/\s+/).filter(Boolean);
  if (t.length < 8) return false;
  return new Set(t).size / t.length < 0.35;
}
function tailRepeat(h) {
  if (!h.length) return 0;
  const k = h[h.length - 1];
  let n = 0;
  for (let i = h.length - 1; i >= 0 && h[i] === k; i--) n++;
  return n;
}
function tailAlt(h) {
  if (h.length < 4) return 0;
  const a = h[h.length - 1], b = h[h.length - 2];
  if (a === b) return 0;
  let n = 0;
  for (let i = h.length - 1; i >= 0; i--) {
    if (h[i] === ((h.length - 1 - i) % 2 === 0 ? a : b)) n++;
    else break;
  }
  return n;
}
function loopCheck(st, p, T) {
  const rep = tailRepeat(st.hist);
  if (rep >= T) {
    writeState(p, { hist: [], streak: 0, seen: st.seen });
    return blockRes(`🛑 guard: ${rep}× подряд одно и то же действие — похоже на зацикливание.\n` +
      `   ${st.hist[st.hist.length - 1].slice(0, 120)}\n` +
      `   Остановись, сверься с планом/TodoWrite, смени подход. Порог: ${T}.`);
  }
  const alt = tailAlt(st.hist);
  if (alt >= 2 * T) {
    writeState(p, { hist: [], streak: 0, seen: st.seen });
    return blockRes(`🛑 guard: чередование двух действий ${alt} шагов подряд (A-B-A-B…) — цикл без прогресса.\n` +
      `   Остановись, сверься с планом/TodoWrite, смени подход. Порог: ${2 * T}.`);
  }
  return null;
}

// ---------- обход харнесса (shell) ----------
// GIT: на Windows `git.exe`/`git.cmd` — валидные вызовы; без вариантов имени
// `git.exe commit --no-verify` проходил мимо блока. Замечание про -n: у git commit
// это --no-verify (обход), у merge/revert — безобидное.
const GIT = "git(?:\\.exe|\\.cmd)?";
const BYPASS = [
  { re: new RegExp(`\\b${GIT}\\s+commit\\b[^\\n]*(?:\\s--no-verify\\b|\\s-[a-z]*n[a-z]*\\b)`, "i"),
    why: "--no-verify / -n на git commit — пропускает pre-commit и commit-msg" },
  { re: new RegExp(`\\b${GIT}\\s+(merge|push)\\b[^\\n]*\\s--no-verify\\b`, "i"),
    why: "--no-verify на git merge/push — пропускает хуки" },
  { re: new RegExp(`\\b${GIT}\\b[^\\n]*\\bcore\\.hookspath\\b`, "i"),
    why: "core.hooksPath (git config или git -c) — отключение/подмена git-хуков" },
  { re: /\blefthook\s+uninstall\b/i,
    why: "lefthook uninstall — снятие всех git-хуков" },
  { re: /(^|[\s;&|])LEFTHOOK\s*=\s*(0|false)\b/i,
    why: "LEFTHOOK=0 — отключение lefthook-хуков (это escape для человека, не для агента)" },
];
function isGitHooksWrite(scrubbed) {
  return /\.git[\/\\]hooks\b/i.test(scrubbed) &&
    /(^|[\s;&|])(rm|mv|cp|tee|chmod|ln|truncate|sed|del|erase|rmdir|rd|move|ren|rename|copy|Remove-Item|Move-Item|Rename-Item|Copy-Item|Set-Content|Add-Content|Clear-Content|Out-File|New-Item)\b|>/i.test(scrubbed);
}
// Обнулить строки в кавычках, чтобы commit -m "про -n флаг" не считался обходом.
function scrubQuotes(cmd) {
  return cmd.replace(/"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'/g, '""');
}

// ---------- основная логика ----------
function run(ctx, env = process.env) {
  try {
    // Fail-closed (всегда включён): обрезанный или нечитаемый payload — отказ
    // решать вслепую. Пустой stdin (ручной запуск) остаётся fail-open в parse().
    if (ctx.truncated || ctx.parseError)
      return blockRes("🛑 guard: входной payload обрезан или нечитаем — защитные проверки не решают вслепую.\n" +
        "   Повтори вызов меньшим изменением (или доставь ввод целиком).");

    const { tool, command, filePath, sessionId, projectDir } = ctx;
    const isShell = /^(bash|shell|sh|exec|run|terminal|execute)/i.test(tool) || (!tool && command);
    const isFile = /^(write|edit|multiedit|applypatch|create|str_replace|notebookedit)/i.test(tool);
    const isRead = /^read/i.test(tool);

    // Пороги циклов; strict — вдвое строже.
    let T_SH = Number(env.HARNESS_LOOP_THRESHOLD) || 5;
    let T_FT = Number(env.HARNESS_TOOLLOOP_THRESHOLD) || 12;
    if (getProfile(env) === "strict") { T_SH = Math.max(2, Math.ceil(T_SH / 2)); T_FT = Math.max(3, Math.ceil(T_FT / 2)); }
    const HIST_MAX = 2 * Math.max(T_SH, T_FT) + 4;

    const p = stateFile(sessionId, projectDir);
    const st = readState(p);
    const cfg = loadConfig(projectDir);
    const notes = [];

    if (isShell && typeof command === "string" && command) {
      const scrubbed = scrubQuotes(command);

      // 1) обход харнесса / запись в защищённые пути и lint-конфиги через shell
      const hit =
        (checkEnabled("bypass", env) &&
          (BYPASS.find((b) => b.re.test(scrubbed)) ||
            (isGitHooksWrite(scrubbed) ? { why: "прямое вмешательство в .git/hooks" } : null))) ||
        (checkEnabled("protected", env) && isProtectedShellWrite(scrubbed, cfg.protected)
          ? { why: "shell-запись в файлы харнесса (hooks/, конфиги, workflows) — их агент не меняет сам" } : null) ||
        (checkEnabled("lintconfig", env) && isLintConfigShellWrite(scrubbed, cfg.lintConfigs)
          ? { why: "shell-запись в lint/format-конфиг — красный гейт чинится кодом, а не ослаблением конфига" } : null);
      if (hit) {
        if (envAllow(env, "HARNESS_ACK_BYPASS")) {
          notes.push(`⚠️ guard: обход харнесса разрешён явно пользователем: ${hit.why}. Обоснуй в отчёте.`);
        } else {
          return blockRes(`🛑 guard: команда обходит harness — заблокировано.\n   Причина: ${hit.why}.\n   ${BYPASS_HINT}`);
        }
      }

      // 1c) запись в файл харнесса через инлайн-eval интерпретатора (node -e/
      // python -c/bash -c…). Write-verb-детекция это не ловит (глагол/путь в
      // строке, scrubQuotes их обнулил), поэтому проверяем СЫРУЮ команду и только
      // напоминаем — жёстко блокировать нельзя (путь в -e может быть безобиден).
      if (checkEnabled("protected", env)) {
        const ip = interpreterProtectedHint(command, cfg.protected);
        if (ip) notes.push(`⚠️ guard: похоже на запись в файл харнесса (${ip}) через инлайн-eval интерпретатора ` +
          `(node -e / python -c / bash -c …). Такой обход не ловится жёстким блоком — не меняй файлы харнесса так. ` +
          `Если это не файл харнесса, игнорируй.`);
      }

      // 2) сбой стриминга / мусор
      if (checkEnabled("corruption", env) && CORRUPTION_RE.test(scrubbed)) {
        writeState(p, { hist: [], streak: 0, seen: st.seen });
        return blockRes(`🛑 guard: мусор tool-разметки в команде — верный признак сбоя стриминга/парсинга.\n   ${JSON.stringify(command.slice(0, 120))}`);
      }
      if (checkEnabled("entropy", env) && isLowEntropy(command)) {
        writeState(p, { hist: [], streak: 0, seen: st.seen });
        return blockRes(`🛑 guard: аномально низкая энтропия токенов команды (паттерн «echo a echo a …»).\n   ${JSON.stringify(command.slice(0, 120))}`);
      }

      // 3) циклы
      if (checkEnabled("loops", env)) {
        st.streak = isTrivial(command) ? st.streak + 1 : 0;
        st.hist.push("sh::" + command.trim());
        if (st.hist.length > HIST_MAX) st.hist.shift();
        if (st.streak >= T_SH) {
          writeState(p, { hist: [], streak: 0, seen: st.seen });
          return blockRes(`🛑 guard: ${st.streak} тривиальных команд подряд (echo/ls/pwd/…) — дегенеративный паттерн.\n` +
            `   Остановись и реши задачу одним осмысленным шагом. Порог: HARNESS_LOOP_THRESHOLD=${T_SH}.`);
        }
        const lr = loopCheck(st, p, T_SH);
        if (lr) return lr;
        writeState(p, st);
      }

      // 4) ранние подсказки про main
      if (checkEnabled("main-note", env)) {
        const branch = currentBranch(projectDir);
        if (["main", "master"].includes(branch) && new RegExp(`\\b${GIT}\\s+(commit|merge)\\b`).test(scrubbed))
          notes.push(`⚠️ guard: git commit/merge на «${branch}» — pre-commit отклонит. Перейди на feature-ветку (релиз: HARNESS_ALLOW_MAIN=1).`);
        if (new RegExp(`\\b${GIT}\\s+push\\b`).test(scrubbed) && /\b(main|master)\b/.test(scrubbed) && !/refs\/tags|v\d/.test(scrubbed))
          notes.push(`⚠️ guard: прямой push в main/master отклонит серверный ruleset. main обновляется через PR.`);
      }
      return allowRes(notes);
    }

    if ((isFile || isRead) && filePath) {
      const rel = normRel(filePath, projectDir);
      const abs = path.isAbsolute(String(filePath)) ? String(filePath) : path.join(String(projectDir), rel);

      // 1) файлы самого харнесса — только с явного разрешения
      if (isFile && checkEnabled("protected", env) && isProtectedPath(rel, cfg.protected)) {
        if (envAllow(env, "HARNESS_ACK_BYPASS")) {
          notes.push(`⚠️ guard: правка файла харнесса (${rel}) разрешена явно пользователем.`);
        } else {
          return blockRes(`🛑 guard: правка файла харнесса заблокирована: ${rel}\n` +
            `   Хуки/конфиги харнесса агент не меняет сам по себе.\n   ${BYPASS_HINT}`);
        }
      }

      // 1b) lint/format-конфиги ЦЕЛЕВОГО проекта: правка существующего — блок
      // (агент «чинит» красный VERIFY, ослабляя конфиг); создание нового — можно.
      if (isFile && checkEnabled("lintconfig", env) &&
          !isProtectedPath(rel, cfg.protected) && isLintConfigPath(rel, cfg.lintConfigs)) {
        let exists = true;
        try { fs.lstatSync(abs); }
        catch (e) { if (e && e.code === "ENOENT") exists = false; } // иные ошибки = fail-closed
        if (exists) {
          if (envAllow(env, "HARNESS_ACK_BYPASS")) {
            notes.push(`⚠️ guard: правка lint-конфига (${rel}) разрешена явно пользователем.`);
          } else {
            return blockRes(`🛑 guard: правка существующего lint/format-конфига заблокирована: ${rel}\n` +
              `   Красный гейт чинится исправлением кода, а не ослаблением конфига.\n` +
              `   Создание нового конфига с нуля разрешено. ${BYPASS_HINT}`);
          }
        }
      }

      // 2) fact-force (ECC GateGuard, note-only): правка существующего файла,
      // который в этой сессии не читали — одна заметка на файл, без спама.
      if (isRead) markSeen(st, rel);
      if (isFile && checkEnabled("fact-force", env) && !st.seen.includes(rel)) {
        let exists = false;
        try { fs.lstatSync(abs); exists = true; } catch {}
        if (exists)
          notes.push(`⚠️ guard: правишь ${rel}, не читав его в этой сессии (EXPLORE → IMPLEMENT). ` +
            `Прочитай файл или места его использования перед правкой.`);
        markSeen(st, rel); // независимо от exists: новый файл дальше «знаком»
      }

      // 3) циклы (Read/Write/Edit одного и того же объекта)
      if (checkEnabled("loops", env)) {
        st.streak = 0;
        st.hist.push(tool.toLowerCase() + "::" + rel);
        if (st.hist.length > HIST_MAX) st.hist.shift();
        const lr = loopCheck(st, p, T_FT);
        if (lr) return lr;
      }
      writeState(p, st);

      // 4) подсказки: DESIGN-стадия и main
      if (isFile) {
        const mockRoot = cfg.mockups.dir.replace(/\\/g, "/").replace(/\/$/, "");
        if (checkEnabled("design-note", env) &&
            !rel.startsWith(mockRoot + "/") && cfg.uiGlobs.map(globToRe).some((re) => re.test(rel)))
          notes.push(`⚠️ guard: правка GUI-файла (${rel}). DESIGN-стадия: ≥${cfg.mockups.min} мокапа + APPROVED до кода ` +
            `(node hooks/new-mockups.js <feature>). Жёсткий гейт — design-gate.js в pre-push/CI.`);
        if (checkEnabled("main-note", env)) {
          const branch = currentBranch(projectDir);
          if (["main", "master"].includes(branch))
            notes.push(`⚠️ guard: правишь файлы на «${branch}». Нужна feature-ветка: git checkout -b feat/…`);
        }
      }
      return allowRes(notes);
    }

    return { exitCode: 0, stdout: "", stderr: "" };
  } catch (e) {
    // fail-open: хук не должен вешать сессию. НО делаем поломку видимой в stderr —
    // иначе исключение в детекторе тихо отключило бы всю защиту для этого вызова.
    return { exitCode: 0, stdout: "", stderr: "⚠️ guard: внутренняя ошибка проверки, пропускаю (fail-open): " + (e && e.message) + "\n" };
  }
}

module.exports = { run };

// ---------- CLI-обёртка ----------
if (require.main === module) {
  (async () => {
    const ctx = await parse();
    const res = run(ctx);
    if (res.stderr) process.stderr.write(res.stderr);
    if (res.stdout) process.stdout.write(res.stdout);
    process.exit(res.exitCode);
  })().catch(() => process.exit(0));
}
