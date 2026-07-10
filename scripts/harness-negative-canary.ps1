[CmdletBinding()]
param(
  [string]$AgentsRoot = "C:\Users\poweruser\projects\llms\agents-main",
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
  $ReportRoot = "C:\Users\poweruser\.codex\automations\dropwheel-pipeline-orchestrator\manual-reports\harness-negative-canary"
}
if (-not $WorktreeRoot) {
  $WorktreeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "dw-neg-canary"
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
    [switch]$RequireJsonOk,
    [switch]$AllowBootstrapJsonFailure
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

    $jsonOk = Get-JsonOkResult $lines
    if ($RequireJsonOk -and $jsonOk.Found -and -not $jsonOk.Ok) {
      $joinedLines = ($lines -join "`n")
      $bootstrapFailure =
        $AllowBootstrapJsonFailure -and
        $joinedLines -match "harness not bootstrapped into repository main" -and
        $joinedLines -match "untracked:"

      if ($bootstrapFailure) {
        $lines += "Accepted setup bootstrap JSON failure; generated files will be staged before doctor."
        $exitCode = 0
      } elseif ($exitCode -eq 0) {
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

function New-MutationStep {
  param(
    [string]$Name,
    [string]$WorkingDirectory,
    [scriptblock]$Mutation
  )

  $started = Get-Date
  $lines = @()
  $exitCode = 0
  try {
    & $Mutation
    $lines += "mutation applied"
  } catch {
    $exitCode = 1
    $lines += $_.Exception.Message
  }

  [pscustomobject]@{
    name = $Name
    command = "local mutation"
    cwd = $WorkingDirectory
    exitCode = $exitCode
    timedOut = $false
    startedAt = $started.ToString("o")
    finishedAt = (Get-Date).ToString("o")
    output = @($lines)
  }
}

function Clip-Lines {
  param([string[]]$Lines, [int]$Max = 120)
  if ($Lines.Count -le $Max) { return $Lines }
  $head = [Math]::Floor($Max / 2)
  $tail = $Max - $head
  return @($Lines[0..($head - 1)] + "... clipped ..." + $Lines[($Lines.Count - $tail)..($Lines.Count - 1)])
}

function Get-NegativeCaseWorktreePath {
  param([string]$Root, [string]$RunId, [string]$Slug)

  $leaf = "$RunId-$Slug"
  $path = Join-Path $Root $leaf
  if ($path.Length -lt 120) { return $path }

  $shortRoot = Join-Path ([System.IO.Path]::GetTempPath()) "dw-neg-canary"
  New-Item -ItemType Directory -Force -Path $shortRoot | Out-Null
  return Join-Path $shortRoot $leaf
}

function Invoke-NegativeCase {
  param(
    [string]$RunId,
    [string]$Slug,
    [string]$Title,
    [scriptblock]$Mutation,
    [string]$ExpectedFailurePattern = ""
  )

  $caseRoot = Get-NegativeCaseWorktreePath $WorktreeRoot $RunId $Slug
  $steps = @()
  $steps += Invoke-CanaryStep "create isolated worktree" "git" @("worktree", "add", "--detach", $caseRoot, "HEAD") $DropwheelRoot

  $setupOk = $steps[-1].exitCode -eq 0
  if ($setupOk) {
    $installer = Join-Path $AgentsRoot "install.js"
    $steps += Invoke-CanaryStep "install latest harness from agents" "node" @($installer, "--target", $caseRoot, "--force", "--json") $AgentsRoot -RequireJsonOk -AllowBootstrapJsonFailure
    $setupOk = $setupOk -and $steps[-1].exitCode -eq 0
  }
  if ($setupOk) {
    $steps += Invoke-CanaryStep "stage generated harness output" "git" @("add", "-A") $caseRoot
    $setupOk = $setupOk -and $steps[-1].exitCode -eq 0
  }
  if ($setupOk) {
    $steps += Invoke-CanaryStep "doctor installed harness" "node" @("hooks\doctor.js", "--json") $caseRoot -RequireJsonOk
    $setupOk = $setupOk -and $steps[-1].exitCode -eq 0
  }
  if ($setupOk) {
    $steps += New-MutationStep "inject $Title" $caseRoot $Mutation
    $setupOk = $setupOk -and $steps[-1].exitCode -eq 0
  }

  $caught = $false
  $expectedFailureMatched = $false
  if ($setupOk) {
    $steps += Invoke-CanaryStep "run verify expecting failure" "node" @("hooks\verify.js") $caseRoot
    $caught = $steps[-1].exitCode -ne 0
    if ($caught) {
      if ([string]::IsNullOrWhiteSpace($ExpectedFailurePattern)) {
        $expectedFailureMatched = $true
      } else {
        $joinedOutput = ($steps[-1].output -join "`n")
        $expectedFailureMatched = $joinedOutput -match $ExpectedFailurePattern
        if (-not $expectedFailureMatched) {
          $steps[-1].output += "Expected failure pattern was not observed: $ExpectedFailurePattern"
        }
      }
    }
  }

  $caseOk = $setupOk -and $caught -and $expectedFailureMatched
  $cleanup = -not $KeepWorktree
  if ($cleanup -and (Test-Path $caseRoot)) {
    $steps += Invoke-CanaryStep "cleanup isolated worktree" "git" @("worktree", "remove", "--force", $caseRoot) $DropwheelRoot
    $caseOk = $caseOk -and $steps[-1].exitCode -eq 0
  }

  [pscustomobject]@{
    slug = $Slug
    title = $Title
    worktree = $caseRoot
    setupOk = $setupOk
    caught = $caught
    expectedFailurePattern = $ExpectedFailurePattern
    expectedFailureMatched = $expectedFailureMatched
    ok = $caseOk
    steps = $steps
  }
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
$runId = "dropwheel-negative-canary-$timestamp"
$reportDir = Join-Path $ReportRoot $runId
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null

$cases = @()
$cases += Invoke-NegativeCase $runId "compile-failure" "C# compile failure" {
  $path = Join-Path $caseRoot "src\Dropwheel\NegativeCanaryCompileBreak.cs"
  @(
    "namespace Dropwheel;"
    ""
    "internal static class NegativeCanaryCompileBreak"
    "{"
    "    public static void Broken()"
    "    {"
    "        this is not valid csharp"
    "    }"
    "}"
  ) | Set-Content -Path $path -Encoding utf8
} "FAIL dotnet/build @ src/Dropwheel|dotnet/build @ src/Dropwheel: exit 1"
$cases += Invoke-NegativeCase $runId "failing-test" "failing xUnit test" {
  $path = Join-Path $caseRoot "tests\Dropwheel.Tests\NegativeCanaryFailTests.cs"
  @(
    "namespace Dropwheel.Tests;"
    ""
    "public sealed class NegativeCanaryFailTests"
    "{"
    "    [Fact]"
    "    public void Harness_negative_canary_must_fail()"
    "    {"
    "        Assert.Fail(""negative canary"");"
    "    }"
    "}"
  ) | Set-Content -Path $path -Encoding utf8
} "FAIL dotnet/test @ tests/Dropwheel.Tests|dotnet/test @ tests/Dropwheel.Tests: exit 1"
$cases += Invoke-NegativeCase $runId "harness-syntax" "broken harness JavaScript syntax" {
  $path = Join-Path $caseRoot "hooks\verify-core.js"
  Add-Content -Path $path -Encoding utf8 -Value ""
  Add-Content -Path $path -Encoding utf8 -Value "this is not valid JavaScript !!!"
} "SyntaxError: Unexpected identifier"

$ok = @($cases | Where-Object { -not $_.ok }).Count -eq 0
$ownerHint = if ($ok) {
  "none"
} elseif (@($cases | Where-Object { $_.setupOk -and -not $_.caught }).Count -gt 0) {
  "agents-harness"
} elseif (@($cases | Where-Object { $_.setupOk -and $_.caught -and -not $_.expectedFailureMatched }).Count -gt 0) {
  "needs-triage"
} elseif (@($cases | Where-Object { -not $_.setupOk }).Count -gt 0) {
  "agents-harness"
} else {
  "needs-triage"
}

$report = [ordered]@{
  schema = "dropwheel-negative-canary/v1"
  runId = $runId
  generatedAt = (Get-Date).ToString("o")
  ok = $ok
  ownerHint = $ownerHint
  agentsRoot = $AgentsRoot
  dropwheelRoot = $DropwheelRoot
  reportDir = $reportDir
  cases = $cases
}

$jsonPath = Join-Path $reportDir "report.json"
$markdownPath = Join-Path $reportDir "report.md"
$report | ConvertTo-Json -Depth 12 | Set-Content -Path $jsonPath -Encoding utf8

$md = @()
$md += "# Dropwheel negative canary report"
$md += ""
$md += "- Run: ``$runId``"
$md += "- OK: ``$ok``"
$md += "- Owner hint: ``$ownerHint``"
$md += "- Agents root: ``$AgentsRoot``"
$md += "- Dropwheel root: ``$DropwheelRoot``"
$md += ""
$md += "## Cases"
foreach ($case in $cases) {
  $md += ""
  $md += "### $($case.title)"
  $md += ""
  $md += "- OK: ``$($case.ok)``"
  $md += "- Setup OK: ``$($case.setupOk)``"
  $md += "- Failure caught: ``$($case.caught)``"
  if ($case.expectedFailurePattern) {
    $md += "- Expected failure matched: ``$($case.expectedFailureMatched)``"
    $md += "- Expected failure pattern: ``$($case.expectedFailurePattern)``"
  }
  $md += "- Worktree: ``$($case.worktree)``"
  foreach ($step in $case.steps) {
    $md += ""
    $md += "#### $($step.name)"
    $md += ""
    $md += "- Exit: ``$($step.exitCode)``"
    $md += "- Command: ``$($step.command)``"
    $md += "- CWD: ``$($step.cwd)``"
    if ($step.exitCode -ne 0 -or $step.name -eq "run verify expecting failure") {
      $md += ""
      $md += '```text'
      $md += Clip-Lines $step.output 120
      $md += '```'
    }
  }
}
$md += ""
$md += "## Triage rule"
$md += ""
$md += "This canary passes only when every injected failure is rejected by `node hooks\verify.js` at the expected verification stage. A case with setup OK and failure not caught is a harness false negative and belongs to `agents`. A case caught at the wrong stage is a negative-canary coverage gap and needs triage."
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
