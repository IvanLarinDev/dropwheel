# Dropwheel Negative Canary Prompt

Verify that the harness catches deliberately injected failures in isolated
Dropwheel worktrees.

## Process

1. Run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\harness-negative-canary.ps1 -MirrorToAgentsInbox
   ```

2. Read the generated `report.md` and `report.json`.
3. Interpret the result:
   - `ok: true`, `ownerHint: none`: every injected failure was caught. Finish
     quietly with the report path.
   - `ownerHint: agents-harness`: a false negative or setup failure points to
     harness behavior. Confirm that the report was mirrored into
     `C:\Users\poweruser\projects\llms\agents\inbox\dropwheel`.
   - `ownerHint: needs-triage`: keep the failing worktree, summarize the
     uncertainty, and ask for user input only if local evidence cannot classify
     it.
4. Do not modify the main Dropwheel checkout. The script must use temporary
   worktrees only.
5. Do not push, merge, release, reset, force-push, weaken harness checks, or use
   bypass environment variables.

## Expected Negative Cases

The script intentionally injects:

- a C# compile failure;
- a failing xUnit test;
- broken harness JavaScript syntax.

The canary passes only when `node hooks\verify.js` rejects all three cases.
