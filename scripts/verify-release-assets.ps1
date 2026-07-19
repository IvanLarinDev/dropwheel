[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string] $Directory,

    [Parameter(Mandatory)]
    [ValidatePattern('^v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$')]
    [string] $Tag,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $ExpectedCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Sha256 {
    param([Parameter(Mandatory)][string] $Path)

    $stream = $null
    $sha256 = $null
    try {
        $stream = [IO.File]::OpenRead($Path)
        $sha256 = [Security.Cryptography.SHA256]::Create()
        return ($sha256.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) -join ''
    } finally {
        if ($null -ne $sha256) { $sha256.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

$contentAssets = @(
    "Dropwheel-$Tag-win-x64.zip",
    "Dropwheel-$Tag-win-x64-self-contained.zip",
    "Dropwheel-$Tag-PROVENANCE.json",
    "Dropwheel-$Tag-SBOM.spdx.json"
)
$checksumAsset = "Dropwheel-$Tag-SHA256SUMS.txt"
$requiredAssets = @($contentAssets) + $checksumAsset
$actualAssets = @(Get-ChildItem -LiteralPath $Directory -File | ForEach-Object Name)

if ($actualAssets.Count -ne $requiredAssets.Count) {
    throw "Expected exactly $($requiredAssets.Count) release assets; found $($actualAssets.Count)."
}
foreach ($name in $requiredAssets) {
    if ($actualAssets -cnotcontains $name) {
        throw "Required release asset '$name' is missing or has incorrect casing."
    }
    if ((Get-Item -LiteralPath (Join-Path $Directory $name)).Length -le 0) {
        throw "Release asset '$name' is empty."
    }
}

$checksumPath = Join-Path $Directory $checksumAsset
$checksumLines = @([IO.File]::ReadAllLines($checksumPath) | Where-Object { $_ })
if ($checksumLines.Count -ne $contentAssets.Count) {
    throw "Expected $($contentAssets.Count) checksum entries; found $($checksumLines.Count)."
}

$expectedHashes = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
foreach ($line in $checksumLines) {
    if ($line -notmatch '^(?<hash>[0-9a-f]{64})  (?<name>[^\\/]+)$') {
        throw "Invalid checksum line '$line'."
    }
    $name = $Matches['name']
    if ($contentAssets -cnotcontains $name) {
        throw "Checksum references unexpected asset '$name'."
    }
    if ($expectedHashes.ContainsKey($name)) {
        throw "Checksum contains duplicate asset '$name'."
    }
    $expectedHashes.Add($name, $Matches['hash'])
}

foreach ($name in $contentAssets) {
    if (-not $expectedHashes.ContainsKey($name)) {
        throw "Checksum is missing asset '$name'."
    }
    $actualHash = Get-Sha256 -Path (Join-Path $Directory $name)
    if (-not [string]::Equals($expectedHashes[$name], $actualHash, [StringComparison]::Ordinal)) {
        throw "Checksum mismatch for '$name'."
    }
}

$provenancePath = Join-Path $Directory "Dropwheel-$Tag-PROVENANCE.json"
$provenance = Get-Content -Raw -LiteralPath $provenancePath | ConvertFrom-Json
if ($provenance.source.tag -cne $Tag) {
    throw "Provenance tag '$($provenance.source.tag)' does not match '$Tag'."
}
if ($provenance.source.commit -cne $ExpectedCommit) {
    throw "Provenance commit '$($provenance.source.commit)' does not match '$ExpectedCommit'."
}

Write-Output "RELEASE_ASSETS_OK tag=$Tag commit=$ExpectedCommit assets=$($requiredAssets.Count) checksums=$($contentAssets.Count)"
