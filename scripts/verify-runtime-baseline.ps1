param(
    [string]$Repo = (Resolve-Path (Join-Path $PSScriptRoot "..")),
    [string]$Dotnet = "dotnet"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$minimum = [Version]"10.0.10"
$output = Join-Path ([IO.Path]::GetTempPath()) ("dropwheel runtime " + [Guid]::NewGuid().ToString("N"))
$frameworkDependentOutput = Join-Path $output "framework-dependent"
$selfContainedOutput = Join-Path $output "self-contained"
$project = Join-Path $Repo "src\Dropwheel\Dropwheel.csproj"

function Get-PublishedArtifact([string]$Root, [string]$FileName) {
    $artifact = Get-ChildItem -Path $Root -Filter $FileName -File -Recurse |
        Where-Object FullName -Match "\\publish\\" |
        Select-Object -ExpandProperty FullName -First 1
    if ([string]::IsNullOrEmpty($artifact)) {
        throw "Runtime probe did not publish $FileName."
    }

    return $artifact
}

function ConvertTo-WindowsProcessArgument([string]$Argument) {
    if ($Argument.Length -gt 0 -and $Argument -notmatch '[\s"]') {
        return $Argument
    }

    $quoted = New-Object Text.StringBuilder
    [void]$quoted.Append([char]'"')
    $backslashes = 0
    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq [char]'\') {
            $backslashes++
            continue
        }

        if ($character -eq [char]'"') {
            [void]$quoted.Append([char]'\', 2 * $backslashes + 1)
            [void]$quoted.Append([char]'"')
            $backslashes = 0
            continue
        }

        if ($backslashes -gt 0) {
            [void]$quoted.Append([char]'\', $backslashes)
            $backslashes = 0
        }
        [void]$quoted.Append($character)
    }

    if ($backslashes -gt 0) {
        [void]$quoted.Append([char]'\', 2 * $backslashes)
    }
    [void]$quoted.Append([char]'"')
    return $quoted.ToString()
}

function Start-NativeProcess([string]$FilePath, [string[]]$Arguments) {
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    if ($null -ne $startInfo.GetType().GetProperty("ArgumentList")) {
        foreach ($argument in @($Arguments)) {
            [void]$startInfo.ArgumentList.Add($argument)
        }
    }
    else {
        $encodedArguments = @($Arguments) | ForEach-Object { ConvertTo-WindowsProcessArgument $_ }
        $startInfo.Arguments = $encodedArguments -join ' '
    }

    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Could not start process: $FilePath"
        }
        return $process
    }
    catch {
        $process.Dispose()
        throw
    }
}

function Stop-ProcessBounded(
    [Diagnostics.Process]$Process,
    [string]$Label,
    [int]$TimeoutMilliseconds = 5000
) {
    if ($null -eq $Process) { return $null }

    $failure = $null
    try {
        if (-not $Process.HasExited) {
            $Process.Kill()
            if (-not $Process.WaitForExit($TimeoutMilliseconds)) {
                $failure = "$Label did not exit within $TimeoutMilliseconds ms after Kill()."
            }
        }
    }
    catch {
        $failure = "$Label cleanup failed: $($_.Exception.Message)"
    }
    finally {
        try {
            $Process.Dispose()
        }
        catch {
            if ($null -eq $failure) {
                $failure = "$Label disposal failed: $($_.Exception.Message)"
            }
        }
    }

    return $failure
}

function Complete-Verification(
    [Management.Automation.ErrorRecord]$VerificationFailure,
    [Management.Automation.ErrorRecord]$OutputCleanupFailure,
    [string[]]$SuccessOutput
) {
    if ($null -ne $VerificationFailure -and $null -ne $OutputCleanupFailure) {
        throw [AggregateException]::new(
            "Runtime verification and temporary output cleanup both failed.",
            [Exception[]]@($VerificationFailure.Exception, $OutputCleanupFailure.Exception))
    }
    if ($null -ne $VerificationFailure) {
        [Runtime.ExceptionServices.ExceptionDispatchInfo]::Capture($VerificationFailure.Exception).Throw()
        return
    }
    if ($null -ne $OutputCleanupFailure) {
        [Runtime.ExceptionServices.ExceptionDispatchInfo]::Capture($OutputCleanupFailure.Exception).Throw()
        return
    }

    $SuccessOutput | Write-Output
}

function Test-LifecycleSmoke(
    [string]$Label,
    [string]$FilePath,
    [string[]]$PrefixArguments = @()
) {
    $smokeRoot = Join-Path $output ("lifecycle-" + $Label)
    $profile = Join-Path $smokeRoot "profile"
    $probe = Join-Path $smokeRoot "ipc-probe.txt"
    $acknowledgement = Join-Path $profile "smoke-ack"
    New-Item -ItemType Directory -Force -Path $profile | Out-Null
    Set-Content -LiteralPath $probe -Value "Dropwheel lifecycle smoke" -Encoding UTF8

    $primary = $null
    $sender = $null
    $smokeFailure = $null
    $cleanupErrors = @()
    $started = [Diagnostics.Stopwatch]::StartNew()
    try {
        $primaryArguments = @($PrefixArguments) + @("--smoke-test", $profile, $probe)
        $primary = Start-NativeProcess $FilePath $primaryArguments
        Start-Sleep -Milliseconds 1000
        $primary.Refresh()
        if ($primary.HasExited) {
            throw "$Label smoke primary exited before IPC delivery with exit code $($primary.ExitCode)."
        }

        $senderArguments = @($PrefixArguments) + @("--smoke-send", $profile, $probe)
        $sender = Start-NativeProcess $FilePath $senderArguments
        if (-not $sender.WaitForExit(5000)) {
            throw "$Label IPC sender did not exit within five seconds."
        }
        if ($sender.ExitCode -ne 0) {
            throw "$Label IPC sender exited with code $($sender.ExitCode)."
        }

        if (-not $primary.WaitForExit(15000)) {
            throw "$Label smoke primary did not exit within 15 seconds after IPC delivery."
        }
        if ($primary.ExitCode -ne 0) {
            throw "$Label smoke primary exited with code $($primary.ExitCode)."
        }

        if (-not (Test-Path $acknowledgement)) {
            throw "$Label lifecycle smoke did not receive an application acknowledgement."
        }
        $acknowledgedProbe = Get-Content -Raw $acknowledgement
        if ($acknowledgedProbe -ne [IO.Path]::GetFullPath($probe)) {
            throw "$Label lifecycle smoke acknowledged an unexpected probe: $acknowledgedProbe"
        }

        $configPath = Join-Path $profile "config.json"
        if (-not (Test-Path $configPath)) {
            throw "$Label lifecycle smoke did not create its isolated config: $configPath"
        }

        $errorLog = Join-Path $profile "error.log"
        if (Test-Path $errorLog) {
            $errors = Get-Content -Raw $errorLog
            if (-not [string]::IsNullOrWhiteSpace($errors)) {
                throw "$Label lifecycle smoke wrote to error.log: $errors"
            }
        }
    }
    catch {
        $smokeFailure = $_
    }
    finally {
        $cleanupErrors = @(
            @(
                Stop-ProcessBounded $sender "$Label IPC sender"
                Stop-ProcessBounded $primary "$Label smoke primary"
            ) | Where-Object { -not [string]::IsNullOrEmpty($_) }
        )
    }

    if ($null -ne $smokeFailure) {
        if ($cleanupErrors.Count -gt 0) {
            $innerExceptions = @($smokeFailure.Exception)
            foreach ($cleanupError in $cleanupErrors) {
                $innerExceptions += [InvalidOperationException]::new($cleanupError)
            }
            throw [AggregateException]::new(
                "$Label lifecycle smoke and process cleanup both failed.",
                [Exception[]]$innerExceptions)
        }

        [Runtime.ExceptionServices.ExceptionDispatchInfo]::Capture($smokeFailure.Exception).Throw()
        return
    }
    if ($cleanupErrors.Count -gt 0) {
        throw ($cleanupErrors -join [Environment]::NewLine)
    }

    $started.Stop()
    return "LIFECYCLE_SMOKE_OK variant=$Label exit=0 elapsed-ms=$($started.ElapsedMilliseconds) ipc=delivered shutdown=graceful profile=isolated probe=matched"
}

$verificationFailure = $null
$outputCleanupFailure = $null
$successOutput = @()
try {
    & $Dotnet publish $project `
        --configuration RuntimeProbe `
        --nologo `
        -p:RestoreLockedMode=true `
        --self-contained false `
        -p:PublishSingleFile=false `
        -p:UseAppHost=false `
        --artifacts-path $frameworkDependentOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Framework-dependent runtime probe publish failed with exit code $LASTEXITCODE."
    }

    $frameworkDependentConfigPath = Get-PublishedArtifact $frameworkDependentOutput "Dropwheel.runtimeconfig.json"
    $frameworkDependentConfig = Get-Content -Raw $frameworkDependentConfigPath | ConvertFrom-Json
    $frameworkDependentDesktop = @($frameworkDependentConfig.runtimeOptions.frameworks) |
        Where-Object name -eq "Microsoft.WindowsDesktop.App" |
        Select-Object -First 1
    if ($null -eq $frameworkDependentDesktop) {
        throw "Framework-dependent runtime config does not reference Microsoft.WindowsDesktop.App."
    }

    $frameworkDependentVersion = [Version]$frameworkDependentDesktop.version
    if ($frameworkDependentVersion -lt $minimum) {
        throw "Windows Desktop Runtime baseline is $frameworkDependentVersion; expected at least $minimum."
    }

    & $Dotnet publish $project `
        --configuration RuntimeProbe `
        --nologo `
        -p:RestoreLockedMode=true `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        --artifacts-path $selfContainedOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Self-contained runtime probe publish failed with exit code $LASTEXITCODE."
    }

    $selfContainedConfigPath = Get-PublishedArtifact $selfContainedOutput "Dropwheel.runtimeconfig.json"
    $selfContainedConfig = Get-Content -Raw $selfContainedConfigPath | ConvertFrom-Json
    $includedFrameworks = @($selfContainedConfig.runtimeOptions.includedFrameworks)
    $selfContainedDesktop = $includedFrameworks |
        Where-Object name -eq "Microsoft.WindowsDesktop.App" |
        Select-Object -First 1
    $selfContainedCore = $includedFrameworks |
        Where-Object name -eq "Microsoft.NETCore.App" |
        Select-Object -First 1
    if ($null -eq $selfContainedDesktop -or $null -eq $selfContainedCore) {
        throw "Self-contained runtime config does not include both NETCore and WindowsDesktop frameworks."
    }

    $selfContainedDesktopVersion = [Version]$selfContainedDesktop.version
    $selfContainedCoreVersion = [Version]$selfContainedCore.version
    if ($selfContainedDesktopVersion -lt $minimum -or $selfContainedCoreVersion -lt $minimum) {
        throw "Self-contained runtime baseline is NETCore=$selfContainedCoreVersion WindowsDesktop=$selfContainedDesktopVersion; expected at least $minimum."
    }

    $depsPath = Get-PublishedArtifact $selfContainedOutput "Dropwheel.deps.json"
    $deps = Get-Content -Raw $depsPath | ConvertFrom-Json
    $libraries = @($deps.libraries.PSObject.Properties.Name)
    $expectedCorePack = "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/$selfContainedCoreVersion"
    $expectedDesktopPack = "runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64/$selfContainedDesktopVersion"
    if ($libraries -notcontains $expectedCorePack -or $libraries -notcontains $expectedDesktopPack) {
        throw "Self-contained deps metadata does not match included runtime framework versions."
    }

    $frameworkDependentDll = Get-PublishedArtifact $frameworkDependentOutput "Dropwheel.dll"
    $successOutput += Test-LifecycleSmoke "framework-dependent" $Dotnet @($frameworkDependentDll)
    $selfContainedExe = Get-PublishedArtifact $selfContainedOutput "Dropwheel.exe"
    $successOutput += Test-LifecycleSmoke "self-contained" $selfContainedExe

    $successOutput += "RUNTIME_BASELINE_OK framework-dependent=$frameworkDependentVersion self-contained-core=$selfContainedCoreVersion self-contained-desktop=$selfContainedDesktopVersion minimum=$minimum"
}
catch {
    $verificationFailure = $_
}
finally {
    try {
        if (Test-Path $output) {
            Remove-Item -Recurse -Force $output
        }
    }
    catch {
        $outputCleanupFailure = $_
    }
}

Complete-Verification $verificationFailure $outputCleanupFailure $successOutput
