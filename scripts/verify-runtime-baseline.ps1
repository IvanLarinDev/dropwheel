param(
    [string]$Repo = (Resolve-Path (Join-Path $PSScriptRoot "..")),
    [string]$Dotnet = "dotnet"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$minimum = [Version]"10.0.10"
$output = Join-Path ([IO.Path]::GetTempPath()) ("dropwheel-runtime-" + [Guid]::NewGuid().ToString("N"))
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

    Write-Output "RUNTIME_BASELINE_OK framework-dependent=$frameworkDependentVersion self-contained-core=$selfContainedCoreVersion self-contained-desktop=$selfContainedDesktopVersion minimum=$minimum"
}
finally {
    if (Test-Path $output) {
        Remove-Item -Recurse -Force $output
    }
}
