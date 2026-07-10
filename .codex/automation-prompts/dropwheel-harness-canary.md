# Dropwheel Harness Canary Automation Prompt

Run the Dropwheel harness canary with minimal user involvement.

1. From the Dropwheel repository, run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\harness-canary.ps1 -MirrorToAgentsInbox
   ```

2. If the command succeeds, report only the report path and archive/finish the
   automation run.
   `-MirrorToAgentsInbox` only mirrors `agents-harness` and `needs-triage`
   failures; Dropwheel-owned reports stay in Dropwheel for auto-fix.

3. If the command fails, read the generated `report.md` and `report.json`.
   Classify the owner:

   - `agents-harness`: do not patch harness files in Dropwheel. Send a concise
     report to the agents harness inbox thread if available, and make sure the
     report files are mirrored under `C:\Users\poweruser\projects\llms\agents\inbox\dropwheel`.
   - `dropwheel-harness-update`: the latest agents installer produced harness
     files that must be accepted in Dropwheel, for example doctor reports
     `harness not bootstrapped ... untracked` for installer-created files such
     as `hooks/verify-core.js`, `hooks/release-preflight.js`, or
     `.github/CODEOWNERS`, while product verify passes. Use
     `.codex/automation-prompts/dropwheel-auto-fix.md` to create a local
     Dropwheel harness update branch/commit from installer output. Do not
     manually patch those files.
   - `dropwheel-or-contract`: inspect the failing build/test output. If it is a
     Dropwheel bug, use `.codex/automation-prompts/dropwheel-auto-fix.md` and
     fix it in Dropwheel when the harness allows it. If it is a harness
     contract bug, route it to agents.
   - `needs-triage`: keep the failing worktree, summarize the uncertainty, and
     ask for user input only if the report cannot be routed.

4. Do not commit, push, force-push, or reset unless the user explicitly asks.
   Leave a short final report with changed files, verification commands, and
   the generated canary report path.
