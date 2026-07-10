[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptPath = Join-Path $PSScriptRoot "pipeline-manifest.ps1"
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("dropwheel-manifest-test-" + [guid]::NewGuid().ToString("N"))
$runRoot = Join-Path $root "run"
$manifestPath = Join-Path $runRoot "manifest.json"
$findingPath = Join-Path $root "finding.json"
$dispositionPath = Join-Path $root "disposition.json"

function Invoke-ManifestProcess {
  param([string[]]$Arguments)
  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = "Continue"
  $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $scriptPath @Arguments 2>&1
  $exitCode = $LASTEXITCODE
  $ErrorActionPreference = $previousPreference
  return [pscustomobject]@{ ExitCode = $exitCode; Output = @($output) }
}

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

try {
  New-Item -ItemType Directory -Force -Path $root | Out-Null
  @{ fingerprint = "agents-harness|pipeline|manifest|disposition-required" } |
    ConvertTo-Json | Set-Content -LiteralPath $findingPath -Encoding utf8
  @{ reason = "Verified and merged"; fixCommit = "abc123"; mergeCommit = "def456" } |
    ConvertTo-Json | Set-Content -LiteralPath $dispositionPath -Encoding utf8

  $result = Invoke-ManifestProcess @("-Mode", "New", "-RunRoot", $runRoot, "-RunId", "test-run")
  Assert-True ($result.ExitCode -eq 0 -and (Test-Path -LiteralPath $manifestPath)) "New manifest failed"

  $result = Invoke-ManifestProcess @(
    "-Mode", "AddFinding", "-ManifestPath", $manifestPath,
    "-WorkerId", "finding-1", "-Owner", "agents-harness", "-Status", "pending",
    "-DataJsonPath", $findingPath
  )
  Assert-True ($result.ExitCode -eq 0) "AddFinding failed"

  $result = Invoke-ManifestProcess @("-Mode", "Complete", "-ManifestPath", $manifestPath, "-Status", "complete")
  Assert-True ($result.ExitCode -ne 0 -and ($result.Output -join "`n") -match "UpdateFindingDisposition") `
    "Complete accepted an undisposed finding"

  $result = Invoke-ManifestProcess @(
    "-Mode", "UpdateFindingDisposition", "-ManifestPath", $manifestPath,
    "-WorkerId", "finding-1", "-Status", "fixed", "-DataJsonPath", $dispositionPath
  )
  Assert-True ($result.ExitCode -eq 0) "UpdateFindingDisposition failed"

  $result = Invoke-ManifestProcess @("-Mode", "Complete", "-ManifestPath", $manifestPath, "-Status", "complete")
  Assert-True ($result.ExitCode -eq 0) "Complete failed after disposition"

  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  Assert-True ($manifest.status -eq "complete") "Manifest status is not complete"
  Assert-True ($manifest.findings[0].status -eq "fixed") "Finding disposition status was not persisted"
  Assert-True (-not [string]::IsNullOrWhiteSpace($manifest.findings[0].dispositionAt)) `
    "Finding disposition timestamp was not persisted"
  Assert-True ($manifest.findings[0].disposition.mergeCommit -eq "def456") `
    "Finding disposition evidence was not persisted"

  Write-Output "PASS: pipeline manifest disposition contract"
} finally {
  Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
