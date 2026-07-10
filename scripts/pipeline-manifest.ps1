[CmdletBinding()]
param(
  [ValidateSet("New", "AddWorker", "AddFinding", "UpdateFindingDisposition", "AddFix", "AddVerification", "AddMerge", "AddEvent", "Complete")]
  [string]$Mode = "New",
  [string]$ManifestPath = "",
  [string]$RunRoot = "",
  [string]$RunId = "",
  [string]$WorkerId = "",
  [string]$Role = "",
  [string]$Owner = "",
  [string]$Status = "",
  [string]$Branch = "",
  [string]$Sha = "",
  [string]$Path = "",
  [string]$ReportPath = "",
  [string]$DataJsonPath = "",
  [string]$DataJson = "{}"
)

$ErrorActionPreference = "Stop"

function Get-ManifestLockName {
  param([string]$Path)
  $fullPath = [System.IO.Path]::GetFullPath($Path).ToLowerInvariant()
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($fullPath)
  $sha = [System.Security.Cryptography.SHA256]::Create()
  try {
    $hash = $sha.ComputeHash($bytes)
  } finally {
    $sha.Dispose()
  }
  $hashText = -join ($hash | ForEach-Object { $_.ToString("x2") })
  return "Local\DropwheelPipelineManifest-$hashText"
}

function Invoke-WithManifestLock {
  param(
    [string]$Path,
    [scriptblock]$Body
  )

  $mutex = [System.Threading.Mutex]::new($false, (Get-ManifestLockName $Path))
  $lockTaken = $false
  try {
    $lockTaken = $mutex.WaitOne([TimeSpan]::FromSeconds(30))
    if (-not $lockTaken) {
      throw "Timed out waiting for manifest lock: $Path"
    }
    & $Body
  } finally {
    if ($lockTaken) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
  }
}

function ConvertTo-Hashtable {
  param([AllowNull()][object]$InputObject)
  if ($null -eq $InputObject) { return $null }
  if ($InputObject -is [System.Collections.IDictionary]) { return $InputObject }
  if ($InputObject -is [array]) {
    $items = [System.Collections.ArrayList]::new()
    foreach ($item in $InputObject) {
      [void]$items.Add((ConvertTo-Hashtable -InputObject $item))
    }
    return ,@($items.ToArray())
  }
  if ($InputObject.GetType().FullName -ne "System.Management.Automation.PSCustomObject") {
    return $InputObject
  }

  $hash = [ordered]@{}
  foreach ($prop in $InputObject.PSObject.Properties) {
    $hash[$prop.Name] = ConvertTo-Hashtable -InputObject $prop.Value
  }
  return $hash
}

function Read-Data {
  if (-not [string]::IsNullOrWhiteSpace($DataJsonPath)) {
    if (-not (Test-Path -LiteralPath $DataJsonPath)) {
      throw "DataJsonPath does not exist: $DataJsonPath"
    }
    $jsonText = Get-Content -Raw -LiteralPath $DataJsonPath
    if ([string]::IsNullOrWhiteSpace($jsonText)) { return [ordered]@{} }
    return ConvertTo-Hashtable ($jsonText | ConvertFrom-Json -ErrorAction Stop)
  }

  if ([string]::IsNullOrWhiteSpace($DataJson)) { return [ordered]@{} }
  return ConvertTo-Hashtable ($DataJson | ConvertFrom-Json -ErrorAction Stop)
}

function Read-Manifest {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path)) { throw "Manifest does not exist: $Path" }
  return ConvertTo-Hashtable (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -ErrorAction Stop)
}

function Write-Manifest {
  param([string]$Path, [object]$Manifest)
  $Manifest.updatedAt = (Get-Date).ToString("o")
  foreach ($collection in @("workers", "findings", "fixes", "verifications", "merges", "events")) {
    if (-not $Manifest.Contains($collection) -or $null -eq $Manifest[$collection]) {
      $Manifest[$collection] = @()
    } elseif ($Manifest[$collection] -is [array]) {
      $Manifest[$collection] = @($Manifest[$collection])
    } elseif ($Manifest[$collection] -is [System.Collections.IDictionary] -and $Manifest[$collection].Count -eq 0) {
      $Manifest[$collection] = @()
    } else {
      $Manifest[$collection] = @($Manifest[$collection])
    }
  }
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
  $tmp = "$Path.tmp"
  $Manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $tmp -Encoding utf8
  Move-Item -LiteralPath $tmp -Destination $Path -Force
}

function New-Record {
  param([string]$Kind, [System.Collections.IDictionary]$Data)
  $record = [ordered]@{
    kind = $Kind
    createdAt = (Get-Date).ToString("o")
  }
  foreach ($key in $Data.Keys) {
    if ($null -ne $Data[$key] -and "$($Data[$key])" -ne "") {
      $record[$key] = $Data[$key]
    }
  }
  return $record
}

function Add-ManifestRecord {
  param(
    [System.Collections.IDictionary]$Manifest,
    [string]$Collection,
    [System.Collections.IDictionary]$Record
  )

  $items = @()
  if ($Manifest.Contains($Collection) -and $null -ne $Manifest[$Collection]) {
    if ($Manifest[$Collection] -is [array]) {
      $items = @($Manifest[$Collection])
    } elseif ($Manifest[$Collection] -is [System.Collections.IDictionary] -and $Manifest[$Collection].Count -eq 0) {
      $items = @()
    } else {
      $items = @($Manifest[$Collection])
    }
  }

  $Manifest[$Collection] = @($items + $Record)
}

if ($Mode -eq "New") {
  if (-not $RunRoot) { throw "-RunRoot is required for Mode=New" }
  if (-not $RunId) { $RunId = Get-Date -Format "yyyyMMdd-HHmmss" }
  if (-not $ManifestPath) { $ManifestPath = Join-Path $RunRoot "manifest.json" }

  Invoke-WithManifestLock $ManifestPath {
    $manifest = [ordered]@{
      schema = "dropwheel-pipeline-run/v1"
      runId = $RunId
      status = "running"
      generatedAt = (Get-Date).ToString("o")
      updatedAt = (Get-Date).ToString("o")
      roots = [ordered]@{
        pipeline = "C:\Users\poweruser\projects\csharp\dropwheel-release"
        dropwheelAccepted = "C:\Users\poweruser\projects\csharp\dropwheel-release"
        agentsDevelopment = "C:\Users\poweruser\projects\llms\agents"
        agentsAccepted = "C:\Users\poweruser\projects\llms\agents-main"
        automation = "C:\Users\poweruser\.codex\automations\dropwheel-pipeline-orchestrator"
      }
      budgets = [ordered]@{
        maxDropwheelFixes = 2
        maxAgentsFixes = 1
        maxFindingsPerRun = 6
      }
      workers = @()
      findings = @()
      fixes = @()
      verifications = @()
      merges = @()
      events = @()
    }
    Write-Manifest $ManifestPath $manifest
  }
  Write-Output $ManifestPath
  exit 0
}

if (-not $ManifestPath) { throw "-ManifestPath is required for Mode=$Mode" }

Invoke-WithManifestLock $ManifestPath {
  $manifest = Read-Manifest $ManifestPath
  if ($Mode -ne "Complete" -and $manifest.status -ne "running") {
    throw "Manifest is already '$($manifest.status)' and cannot accept Mode=${Mode}: $ManifestPath"
  }
  $data = Read-Data

  switch ($Mode) {
    "AddWorker" {
      Add-ManifestRecord $manifest "workers" (New-Record "worker" ([ordered]@{
        id = $WorkerId
        role = $Role
        owner = $Owner
        status = $Status
        reportPath = $ReportPath
        data = $data
      }))
    }
    "AddFinding" {
      Add-ManifestRecord $manifest "findings" (New-Record "finding" ([ordered]@{
        id = $WorkerId
        owner = $Owner
        status = $Status
        path = $Path
        reportPath = $ReportPath
        data = $data
      }))
    }
    "UpdateFindingDisposition" {
      if ([string]::IsNullOrWhiteSpace($WorkerId)) {
        throw "-WorkerId must identify a finding id or fingerprint"
      }
      if ($Status -notin @("fixed", "open", "deferred")) {
        throw "Finding disposition status must be fixed, open, or deferred"
      }
      if ($data.Count -eq 0) {
        throw "Finding disposition requires non-empty DataJson or DataJsonPath evidence"
      }

      $matches = @($manifest.findings | Where-Object {
        $idMatches = $_.Contains("id") -and $_.id -eq $WorkerId
        $fingerprintMatches = $_.Contains("data") -and
          $_.data -is [System.Collections.IDictionary] -and
          $_.data.Contains("fingerprint") -and $_.data.fingerprint -eq $WorkerId
        $idMatches -or $fingerprintMatches
      })
      if ($matches.Count -ne 1) {
        throw "Expected exactly one finding for '$WorkerId'; found $($matches.Count)"
      }

      $finding = $matches[0]
      $finding["status"] = $Status
      $finding["dispositionAt"] = (Get-Date).ToString("o")
      $finding["disposition"] = $data
    }
    "AddFix" {
      Add-ManifestRecord $manifest "fixes" (New-Record "fix" ([ordered]@{
        id = $WorkerId
        owner = $Owner
        status = $Status
        branch = $Branch
        sha = $Sha
        path = $Path
        reportPath = $ReportPath
        data = $data
      }))
    }
    "AddVerification" {
      Add-ManifestRecord $manifest "verifications" (New-Record "verification" ([ordered]@{
        id = $WorkerId
        owner = $Owner
        status = $Status
        branch = $Branch
        sha = $Sha
        reportPath = $ReportPath
        data = $data
      }))
    }
    "AddMerge" {
      Add-ManifestRecord $manifest "merges" (New-Record "merge" ([ordered]@{
        id = $WorkerId
        owner = $Owner
        status = $Status
        branch = $Branch
        sha = $Sha
        path = $Path
        reportPath = $ReportPath
        data = $data
      }))
    }
    "AddEvent" {
      Add-ManifestRecord $manifest "events" (New-Record "event" ([ordered]@{
        id = $WorkerId
        role = $Role
        owner = $Owner
        status = $Status
        path = $Path
        reportPath = $ReportPath
        data = $data
      }))
    }
    "Complete" {
      $undisposed = @($manifest.findings | Where-Object {
        -not $_.Contains("dispositionAt") -or $_.status -notin @("fixed", "open", "deferred")
      })
      if ($undisposed.Count -gt 0) {
        $ids = @($undisposed | ForEach-Object {
          if ($_.Contains("id")) { $_.id } else { "<missing-id>" }
        }) -join ", "
        throw "Every finding requires UpdateFindingDisposition before Complete: $ids"
      }
      if (-not $Status) { $Status = "complete" }
      if ($manifest.status -ne "running" -and $manifest.status -ne $Status) {
        throw "Manifest is already '$($manifest.status)' and cannot be completed as '$Status': $ManifestPath"
      }
      $manifest.status = $Status
      $manifest.completedAt = (Get-Date).ToString("o")
      Add-ManifestRecord $manifest "events" (New-Record "event" ([ordered]@{
        id = "complete"
        status = $Status
        data = $data
      }))
    }
  }

  Write-Manifest $ManifestPath $manifest
}
Write-Output $ManifestPath
