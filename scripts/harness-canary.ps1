[CmdletBinding()]
param(
  [string]$AgentsRoot = "C:\Users\poweruser\projects\llms\agents",
  [string]$DropwheelRoot = "",
  [string]$ReportRoot = "",
  [string]$WorktreeRoot = "",
  [int]$StepTimeoutSeconds = 900,
  [switch]$KeepWorktree,
  [switch]$MirrorToAgentsInbox,
  [string]$AgentsInboxRoot = ""
)

$ErrorActionPreference = "Stop"

if (-not $DropwheelRoot) {
  $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
  $DropwheelRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
}
if (-not $ReportRoot) {
  $ReportRoot = "C:\Users\poweruser\.codex\automations\dropwheel-pipeline-orchestrator\manual-reports\harness-canary"
}
if (-not $WorktreeRoot) {
  $WorktreeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "dw-canary"
}
if (-not $AgentsInboxRoot) {
  $AgentsInboxRoot = "C:\Users\poweruser\projects\llms\agents\inbox\dropwheel"
}

function Format-Command {
  param([string]$File, [string[]]$Arguments)
  return (@($File) + $Arguments) -join " "
}

function Quote-ProcessArgument {
  param([string]$Argument)
  if ($null -eq $Argument) { return '""' }
  if ($Argument.Length -eq 0) { return '""' }
  if ($Argument -notmatch '[\s"]') { return $Argument }

  $result = '"'
  $slashes = 0
  foreach ($ch in $Argument.ToCharArray()) {
    if ($ch -eq '\') {
      $slashes += 1
    } elseif ($ch -eq '"') {
      $result += ('\' * ($slashes * 2 + 1))
      $result += '"'
      $slashes = 0
    } else {
      if ($slashes -gt 0) { $result += ('\' * $slashes); $slashes = 0 }
      $result += $ch
    }
  }
  if ($slashes -gt 0) { $result += ('\' * ($slashes * 2)) }
  $result += '"'
  return $result
}

function Join-ProcessArguments {
  param([string[]]$Arguments)
  return (($Arguments | ForEach-Object { Quote-ProcessArgument ([string]$_) }) -join " ")
}

function Stop-ProcessTree {
  param([int]$ProcessId)
  $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $ProcessId" -ErrorAction SilentlyContinue)
  foreach ($child in $children) {
    Stop-ProcessTree -ProcessId ([int]$child.ProcessId)
  }
  Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

function Get-JsonOkResult {
  param([string[]]$Lines)
  foreach ($line in $Lines) {
    $trimmed = $line.Trim()
    if (-not $trimmed.StartsWith("{")) { continue }
    try {
      $parsed = $trimmed | ConvertFrom-Json -ErrorAction Stop
      $okProp = $parsed.PSObject.Properties["ok"]
      if ($null -ne $okProp) {
        $reason = ""
        $reasonProp = $parsed.PSObject.Properties["reason"]
        if ($null -ne $reasonProp) { $reason = [string]$reasonProp.Value }
        return [pscustomobject]@{
          Found = $true
          Ok = [bool]$okProp.Value
          Reason = $reason
        }
      }
    } catch {
      continue
    }
  }
  return [pscustomobject]@{
    Found = $false
    Ok = $true
    Reason = ""
  }
}

function Invoke-CanaryStep {
  param(
    [string]$Name,
    [string]$File,
    [string[]]$Arguments,
    [string]$WorkingDirectory,
    [switch]$RequireJsonOk
  )

  $started = Get-Date
  $lines = @()
  $exitCode = 0
  $timedOut = $false

  try {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $File
    $psi.Arguments = Join-ProcessArguments $Arguments
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timeoutMs = [Math]::Max(1, $StepTimeoutSeconds) * 1000
    if (-not $process.WaitForExit($timeoutMs)) {
      $timedOut = $true
      Stop-ProcessTree -ProcessId $process.Id
      $process.WaitForExit(5000) | Out-Null
      $exitCode = 124
    } else {
      $process.WaitForExit()
      $exitCode = $process.ExitCode
    }
    $stdoutText = $stdoutTask.Result
    $stderrText = $stderrTask.Result
    $stdout = @(if ($stdoutText) { $stdoutText -split "\r?\n" } else { @() })
    $stderr = @(if ($stderrText) { $stderrText -split "\r?\n" } else { @() })
    $lines = @($stdout + $stderr | Where-Object { $_ -ne $null } | ForEach-Object { $_.ToString() })
    if ($timedOut) {
      $lines += "Timed out after $StepTimeoutSeconds second(s)."
    }
    if ($RequireJsonOk -and $exitCode -eq 0) {
      $jsonOk = Get-JsonOkResult $lines
      if ($jsonOk.Found -and -not $jsonOk.Ok) {
        $exitCode = 1
        $message = "Semantic failure: command returned JSON ok=false."
        if ($jsonOk.Reason) { $message += " Reason: $($jsonOk.Reason)" }
        $lines += $message
      }
    }
  } catch {
    $lines = @($_.Exception.Message)
    $exitCode = 1
  }

  [pscustomobject]@{
    name = $Name
    command = Format-Command $File $Arguments
    cwd = $WorkingDirectory
    exitCode = $exitCode
    timedOut = $timedOut
    startedAt = $started.ToString("o")
    finishedAt = (Get-Date).ToString("o")
    output = @($lines)
  }
}

function Invoke-GitText {
  param([string]$Root, [string[]]$Arguments)
  Push-Location $Root
  try {
    $out = & git @Arguments 2>$null
    return (($out | ForEach-Object { $_.ToString() }) -join "`n").Trim()
  } catch {
    return ""
  } finally {
    Pop-Location
  }
}

function Get-GitInfo {
  param([string]$Root)
  $statusText = Invoke-GitText $Root @("status", "--short")
  $status = @()
  if ($statusText) {
    $status = @($statusText -split "\r?\n" | Where-Object { $_ })
  }
  [ordered]@{
    root = $Root
    branch = Invoke-GitText $Root @("branch", "--show-current")
    sha = Invoke-GitText $Root @("rev-parse", "HEAD")
    status = $status
  }
}

function New-CanaryStepResult {
  param(
    [string]$Name,
    [string]$Command,
    [string]$WorkingDirectory,
    [int]$ExitCode,
    [string[]]$Output,
    [datetime]$StartedAt = (Get-Date)
  )

  [pscustomobject]@{
    name = $Name
    command = $Command
    cwd = $WorkingDirectory
    exitCode = $ExitCode
    timedOut = $false
    startedAt = $StartedAt.ToString("o")
    finishedAt = (Get-Date).ToString("o")
    output = @($Output)
  }
}

function Get-OwnerHint {
  param([object[]]$Steps)
  $failed = @($Steps | Where-Object { $_.exitCode -ne 0 -and $_.name -ne "cleanup isolated worktree" })
  if ($failed.Count -eq 0) {
    $cleanupFailed = @($Steps | Where-Object { $_.exitCode -ne 0 -and $_.name -eq "cleanup isolated worktree" })
    if ($cleanupFailed.Count -gt 0) { return "pipeline-cleanup" }
    return "none"
  }

  $first = $failed[0].name
  $joined = (($failed | ForEach-Object { $_.output }) -join "`n")

  if ($first -eq "validate dropwheel root clean") {
    return "pipeline-precondition"
  }
  if ($joined -match "harness not bootstrapped into repository main" -and
      $joined -match "untracked:" -and
      $joined -match "hooks[/\\]verify-core\.js|hooks[/\\]release-preflight\.js|\.github[/\\]CODEOWNERS") {
    return "dropwheel-harness-update"
  }
  if ($first -match "install|doctor" -or $joined -match "hooks[/\\]|harness|install\.js|doctor\.js|verify\.js") {
    return "agents-harness"
  }
  if ($joined -match "dotnet|Dropwheel|\.csproj|xUnit|tests[/\\]Dropwheel") {
    return "dropwheel-or-contract"
  }
  return "needs-triage"
}

function Get-CanaryWorktreePath {
  param([string]$Root, [string]$RunId)

  $path = Join-Path $Root $RunId
  if ($path.Length -lt 120) { return $path }

  $shortRoot = Join-Path ([System.IO.Path]::GetTempPath()) "dw-canary"
  New-Item -ItemType Directory -Force -Path $shortRoot | Out-Null
  return Join-Path $shortRoot $RunId
}

function Clip-Lines {
  param([string[]]$Lines, [int]$Max = 160)
  if ($Lines.Count -le $Max) { return $Lines }
  $head = [Math]::Floor($Max / 2)
  $tail = $Max - $head
  return @($Lines[0..($head - 1)] + "... clipped ..." + $Lines[($Lines.Count - $tail)..($Lines.Count - 1)])
}

if (-not (Test-Path $AgentsRoot)) {
  throw "AgentsRoot does not exist: $AgentsRoot"
}
if (-not (Test-Path $DropwheelRoot)) {
  throw "DropwheelRoot does not exist: $DropwheelRoot"
}

New-Item -ItemType Directory -Force -Path $ReportRoot | Out-Null
New-Item -ItemType Directory -Force -Path $WorktreeRoot | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runId = "dropwheel-canary-$timestamp"
$reportDir = Join-Path $ReportRoot $runId
$worktree = Get-CanaryWorktreePath $WorktreeRoot $runId
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null

$steps = @()
$sourceInfo = Get-GitInfo $DropwheelRoot
if (@($sourceInfo.status).Count -gt 0) {
  $steps += New-CanaryStepResult `
    "validate dropwheel root clean" `
    "git status --short" `
    $DropwheelRoot `
    1 `
    (@("DropwheelRoot has uncommitted changes. Commit/stash/revert generated artifacts before canary so the isolated worktree verifies the intended HEAD.") + @($sourceInfo.status))
} else {
  $steps += New-CanaryStepResult "validate dropwheel root clean" "git status --short" $DropwheelRoot 0 @("clean")
}

if ($steps[-1].exitCode -eq 0) {
  $steps += Invoke-CanaryStep "create isolated dropwheel worktree" "git" @("worktree", "add", "--detach", $worktree, "HEAD") $DropwheelRoot
}

if ($steps[-1].exitCode -eq 0) {
  $installer = Join-Path $AgentsRoot "install.js"
  $steps += Invoke-CanaryStep "install latest harness from agents" "node" @($installer, "--target", $worktree, "--force", "--json") $AgentsRoot -RequireJsonOk
  $steps += Invoke-CanaryStep "doctor installed harness" "node" @("hooks\doctor.js", "--json") $worktree -RequireJsonOk
  $steps += Invoke-CanaryStep "show verify plan" "node" @("hooks\verify.js", "--list") $worktree
  $steps += Invoke-CanaryStep "run verify" "node" @("hooks\verify.js") $worktree
  $steps += Invoke-CanaryStep "capture worktree status" "git" @("status", "--short") $worktree
}

if (-not $KeepWorktree -and (Test-Path $worktree)) {
  $steps += Invoke-CanaryStep "cleanup isolated worktree" "git" @("worktree", "remove", "--force", $worktree) $DropwheelRoot
}

$criticalFailures = @($steps | Where-Object {
  $_.exitCode -ne 0 -and $_.name -ne "capture worktree status"
})
$ok = $criticalFailures.Count -eq 0

$ownerHint = Get-OwnerHint $steps
$report = [ordered]@{
  schema = "dropwheel-harness-canary/v1"
  runId = $runId
  generatedAt = (Get-Date).ToString("o")
  ok = $ok
  ownerHint = $ownerHint
  agents = Get-GitInfo $AgentsRoot
  dropwheel = Get-GitInfo $DropwheelRoot
  worktree = $worktree
  reportDir = $reportDir
  steps = $steps
}

$jsonPath = Join-Path $reportDir "report.json"
$markdownPath = Join-Path $reportDir "report.md"
$report | ConvertTo-Json -Depth 10 | Set-Content -Path $jsonPath -Encoding utf8

$md = @()
$md += "# Dropwheel harness canary report"
$md += ""
$md += "- Run: ``$runId``"
$md += "- OK: ``$ok``"
$md += "- Owner hint: ``$ownerHint``"
$md += "- Agents SHA: ``$($report.agents.sha)``"
$md += "- Dropwheel SHA: ``$($report.dropwheel.sha)``"
$md += "- Worktree: ``$worktree``"
$md += ""
$md += "## Steps"
foreach ($step in $steps) {
  $md += ""
  $md += "### $($step.name)"
  $md += ""
  $md += "- Exit: ``$($step.exitCode)``"
  $md += "- Command: ``$($step.command)``"
  $md += "- CWD: ``$($step.cwd)``"
  if ($step.exitCode -ne 0) {
    $md += ""
    $md += '```text'
    $md += Clip-Lines $step.output 160
    $md += '```'
  }
}
$md += ""
$md += "## Triage rule"
$md += ""
$md += 'If install/doctor/harness syntax fails, fix `agents`. If install/doctor is green and only Dropwheel build/tests fail, fix `dropwheel` unless the failure proves a bad harness contract. If unclear, send this report to the `agents` harness inbox.'
$md | Set-Content -Path $markdownPath -Encoding utf8

if ($MirrorToAgentsInbox) {
  if ($ownerHint -eq "agents-harness" -or $ownerHint -eq "needs-triage") {
    New-Item -ItemType Directory -Force -Path $AgentsInboxRoot | Out-Null
    Copy-Item -Path $jsonPath -Destination (Join-Path $AgentsInboxRoot "$runId.json") -Force
    Copy-Item -Path $markdownPath -Destination (Join-Path $AgentsInboxRoot "$runId.md") -Force
    Write-Host "mirrored: $AgentsInboxRoot"
  } else {
    Write-Host "not mirrored to agents inbox: ownerHint $ownerHint"
  }
}

Write-Host "report: $markdownPath"
Write-Host "json: $jsonPath"
Write-Host "ownerHint: $ownerHint"

if ($ok) { exit 0 }
exit 1
