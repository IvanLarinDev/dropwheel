<#
.SYNOPSIS
Creates, verifies, and publishes a Dropwheel SemVer release.

.DESCRIPTION
The script always prepares a release in an isolated temporary worktree based on
the latest origin/main. It updates only the project version and CHANGELOG, runs
tests and publish smoke checks, pushes a fast-forward release commit to main,
waits for CI, then creates an annotated tag and verifies the GitHub Release.

Use an exact version equal to the version already on main to resume an
interrupted release after the release commit has been pushed.

.EXAMPLE
pwsh ./scripts/release.ps1 -Bump patch -DryRun

.EXAMPLE
pwsh ./scripts/release.ps1 -Bump minor

.EXAMPLE
pwsh ./scripts/release.ps1 -Bump 0.16.0
#>
[CmdletBinding()]
param(
    [ValidatePattern('^(patch|minor|major|(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))$')]
    [string] $Bump = 'patch',

    [switch] $DryRun,

    [ValidateRange(1, 240)]
    [int] $TimeoutMinutes = 45,

    [ValidateRange(2, 60)]
    [int] $PollSeconds = 10,

    [string] $Remote = 'origin',

    [string] $Branch = 'main'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$ProjectPath = 'src/Dropwheel/Dropwheel.csproj'
$ChangelogPath = 'CHANGELOG.md'
$CiWorkflow = 'ci.yml'
$ReleaseWorkflow = 'release.yml'
$AllowedReleaseFiles = @($ChangelogPath, $ProjectPath)
$SemVerPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$'

function Assert-Tool {
    param([Parameter(Mandatory)][string] $Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required tool '$Name' was not found on PATH."
    }
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [Parameter(Mandatory)][string[]] $ArgumentList,
        [switch] $Capture
    )

    if ($Capture) {
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            # Windows PowerShell 5 turns redirected native stderr into error
            # records. Keep it non-terminating so the native exit code remains
            # the source of truth, as it is in PowerShell 7.
            $ErrorActionPreference = 'Continue'
            $output = @(& $FilePath @ArgumentList 2>&1)
            $exitCode = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        $text = ($output | ForEach-Object { $_.ToString() }) -join "`n"
        if ($exitCode -ne 0) {
            throw "Command failed ($exitCode): $FilePath $($ArgumentList -join ' ')`n$text"
        }
        return $text.TrimEnd()
    }

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed ($LASTEXITCODE): $FilePath $($ArgumentList -join ' ')"
    }
}

function Test-GitRef {
    param([Parameter(Mandatory)][string] $Ref)

    & git show-ref --verify --quiet $Ref
    return $LASTEXITCODE -eq 0
}

function Test-RemoteTag {
    param(
        [Parameter(Mandatory)][string] $RemoteName,
        [Parameter(Mandatory)][string] $Tag
    )

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & git ls-remote --exit-code --tags $RemoteName "refs/tags/$Tag" *> $null
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    return $exitCode -eq 0
}

function Test-GitAncestor {
    param(
        [Parameter(Mandatory)][string] $Ancestor,
        [Parameter(Mandatory)][string] $Descendant
    )

    & git merge-base --is-ancestor $Ancestor $Descendant
    return $LASTEXITCODE -eq 0
}

function Test-GitHubReleaseExists {
    param([Parameter(Mandatory)][string] $Tag)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & gh release view $Tag --json tagName *> $null
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    return $exitCode -eq 0
}

function Get-VersionFromProjectText {
    param([Parameter(Mandatory)][string] $Content)

    $matches = [regex]::Matches($Content, '<Version>([^<]+)</Version>')
    if ($matches.Count -ne 1) {
        throw "Expected exactly one <Version> element in $ProjectPath; found $($matches.Count)."
    }

    $version = $matches[0].Groups[1].Value
    if ($version -notmatch $SemVerPattern) {
        throw "Project version '$version' is not a stable X.Y.Z SemVer."
    }
    return $version
}

function Get-ProjectVersionAtRef {
    param([Parameter(Mandatory)][string] $Ref)

    $content = Invoke-Native git @('show', "${Ref}:$ProjectPath") -Capture
    return Get-VersionFromProjectText $content
}

function Get-NextVersion {
    param(
        [Parameter(Mandatory)][string] $Current,
        [Parameter(Mandatory)][string] $RequestedBump
    )

    if ($RequestedBump -match $SemVerPattern) {
        return $RequestedBump
    }

    $parts = $Current.Split('.') | ForEach-Object { [int64] $_ }
    switch ($RequestedBump) {
        'major' { return "$(($parts[0] + 1)).0.0" }
        'minor' { return "$($parts[0]).$(($parts[1] + 1)).0" }
        'patch' { return "$($parts[0]).$($parts[1]).$(($parts[2] + 1))" }
        default { throw "Unsupported bump '$RequestedBump'." }
    }
}

function Compare-SemVer {
    param(
        [Parameter(Mandatory)][string] $Left,
        [Parameter(Mandatory)][string] $Right
    )

    $leftParts = $Left.Split('.') | ForEach-Object { [int64] $_ }
    $rightParts = $Right.Split('.') | ForEach-Object { [int64] $_ }
    for ($index = 0; $index -lt 3; $index++) {
        if ($leftParts[$index] -lt $rightParts[$index]) { return -1 }
        if ($leftParts[$index] -gt $rightParts[$index]) { return 1 }
    }
    return 0
}

function Get-ReleaseCommit {
    param(
        [Parameter(Mandatory)][string] $MainRef,
        [Parameter(Mandatory)][string] $Version
    )

    $subject = "chore(release): Dropwheel $Version"
    $log = Invoke-Native git @(
        'log', $MainRef, '--format=%H%x09%s', '--fixed-strings', "--grep=$subject"
    ) -Capture
    $matches = @($log -split "`n" | Where-Object {
        $_ -match '^([0-9a-f]{40})\t(.+)$' -and $Matches[2] -eq $subject
    })
    if ($matches.Count -eq 0) {
        throw "Version $Version is already on main, but its release commit was not found."
    }

    $shas = @($matches | ForEach-Object { ($_ -split "`t", 2)[0] } | Select-Object -Unique)
    if ($shas.Count -ne 1) {
        throw "Found multiple release commits for version $Version; refusing to guess."
    }
    return $shas[0]
}

function ConvertTo-MarkdownText {
    param([AllowEmptyString()][string] $Text)

    return $Text.Replace('\', '\\').Replace('[', '\[').Replace(']', '\]')
}

function New-ChangelogSection {
    param(
        [Parameter(Mandatory)][string] $PreviousTag,
        [Parameter(Mandatory)][string] $TargetTag,
        [Parameter(Mandatory)][string] $BaseSha,
        [Parameter(Mandatory)][string] $Repository,
        [Parameter(Mandatory)][string] $NewLine
    )

    $rawLog = Invoke-Native git @(
        'log', "$PreviousTag..$BaseSha", '--no-merges',
        '--pretty=format:%H%x09%an%x09%s'
    ) -Capture
    $commits = @($rawLog -split "`n" | Where-Object { $_ })
    if ($commits.Count -eq 0) {
        throw "There are no commits between $PreviousTag and $BaseSha."
    }

    $headings = [ordered]@{
        feat     = 'Features'
        fix      = 'Bug Fixes'
        perf     = 'Performance'
        refactor = 'Refactoring'
        docs     = 'Documentation'
        test     = 'Tests'
        build    = 'Build System'
        ci       = 'Continuous Integration'
        chore    = 'Miscellaneous Chores'
        other    = 'Other Changes'
    }
    $groups = [ordered]@{}
    foreach ($key in $headings.Keys) {
        $groups[$key] = [System.Collections.Generic.List[string]]::new()
    }

    foreach ($line in $commits) {
        $parts = $line -split "`t", 3
        if ($parts.Count -ne 3) {
            throw "Could not parse git log line: $line"
        }
        $sha, $author, $subject = $parts
        if ($subject -like 'chore(release): Dropwheel *') {
            continue
        }

        $type = 'other'
        $scope = $null
        $description = $subject
        if ($subject -match '^(?<type>[a-zA-Z]+)(?:\((?<scope>[^)]+)\))?!?:\s+(?<description>.+)$') {
            $candidate = $Matches['type'].ToLowerInvariant()
            if ($headings.Contains($candidate)) {
                $type = $candidate
            }
            $scope = $Matches['scope']
            $description = $Matches['description']
        }

        $shortSha = $sha.Substring(0, 7)
        $prefix = if ($scope) { "(**$(ConvertTo-MarkdownText $scope)**) " } else { '' }
        $bullet = "- $prefix$(ConvertTo-MarkdownText $description) - ([$shortSha](https://github.com/$Repository/commit/$sha)) - $(ConvertTo-MarkdownText $author)"
        $groups[$type].Add($bullet)
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('- - -')
    $lines.Add("## [$TargetTag](https://github.com/$Repository/compare/$PreviousTag..$TargetTag) - $([DateTime]::Now.ToString('yyyy-MM-dd'))")
    foreach ($key in $headings.Keys) {
        if ($groups[$key].Count -eq 0) { continue }
        $lines.Add("#### $($headings[$key])")
        foreach ($bullet in $groups[$key]) {
            $lines.Add($bullet)
        }
    }

    if ($lines.Count -eq 2) {
        throw 'All commits were filtered from the changelog; refusing to create an empty release.'
    }
    return $lines -join $NewLine
}

function Assert-ReleaseFileAllowlist {
    $status = Invoke-Native git @('status', '--porcelain=v1', '--untracked-files=all') -Capture
    $entries = @($status -split "`n" | Where-Object { $_ })
    if ($entries.Count -eq 0) {
        throw 'Release preparation produced no file changes.'
    }

    $paths = @($entries | ForEach-Object {
        if ($_.Length -lt 4) { throw "Could not parse git status entry: $_" }
        $path = $_.Substring(3)
        if ($path.Contains(' -> ')) { $path = ($path -split ' -> ', 2)[1] }
        $path.Replace('\', '/')
    })
    $unexpected = @($paths | Where-Object { $_ -notin $AllowedReleaseFiles })
    if ($unexpected.Count -gt 0) {
        throw "Release changed files outside the allowlist: $($unexpected -join ', ')"
    }
    foreach ($requiredPath in $AllowedReleaseFiles) {
        if ($requiredPath -notin $paths) {
            throw "Expected release file '$requiredPath' was not changed."
        }
    }
}

function Wait-ForWorkflowRun {
    param(
        [Parameter(Mandatory)][string] $Workflow,
        [Parameter(Mandatory)][string] $CommitSha,
        [Parameter(Mandatory)][DateTime] $Deadline
    )

    $reportedRun = $null
    while ([DateTime]::UtcNow -lt $Deadline) {
        $json = Invoke-Native gh @(
            'run', 'list', '--workflow', $Workflow, '--commit', $CommitSha,
            '--limit', '20', '--json', 'databaseId,status,conclusion,headSha,url,createdAt'
        ) -Capture
        # Windows PowerShell 5 emits an empty JSON array as one pipeline item
        # whose value is an empty Object[]. Explicit enumeration prevents it
        # from looking like a run with no databaseId under StrictMode.
        $decodedRuns = $json | ConvertFrom-Json
        $runs = @(
            $decodedRuns |
                ForEach-Object { $_ } |
                Where-Object { $null -ne $_ } |
                Sort-Object createdAt -Descending
        )
        if ($runs.Count -gt 0) {
            $run = $runs[0]
            if ($reportedRun -ne $run.databaseId) {
                Write-Host "Found $Workflow run $($run.databaseId): $($run.url)"
                $reportedRun = $run.databaseId
            }
            if ($run.status -eq 'completed') {
                if ($run.conclusion -ne 'success') {
                    throw "$Workflow run $($run.databaseId) completed with '$($run.conclusion)': $($run.url)"
                }
                Write-Host "$Workflow succeeded: $($run.url)"
                return $run
            }
        }
        Start-Sleep -Seconds $PollSeconds
    }
    throw "Timed out waiting for $Workflow on commit $CommitSha."
}

function Start-OrResumeReleaseWorkflow {
    param(
        [Parameter(Mandatory)][string] $Tag,
        [Parameter(Mandatory)][string] $CommitSha,
        [Parameter(Mandatory)][DateTime] $Deadline
    )

    $json = Invoke-Native gh @(
        'run', 'list', '--workflow', $ReleaseWorkflow, '--commit', $CommitSha,
        '--limit', '20', '--json', 'databaseId,status,conclusion,headSha,url,createdAt'
    ) -Capture
    $decodedRuns = $json | ConvertFrom-Json
    $runs = @(
        $decodedRuns |
            ForEach-Object { $_ } |
            Where-Object { $null -ne $_ } |
            Sort-Object createdAt -Descending
    )
    if ($runs.Count -eq 0) {
        Write-Host "No release workflow run found; dispatching $ReleaseWorkflow for $Tag."
        Invoke-Native gh @('workflow', 'run', $ReleaseWorkflow, '--ref', $Tag, '-f', "tag=$Tag")
    } else {
        $run = $runs[0]
        if ($run.status -eq 'completed' -and $run.conclusion -ne 'success') {
            Write-Host "Rerunning failed release workflow $($run.databaseId)."
            Invoke-Native gh @('run', 'rerun', $run.databaseId.ToString())
            Start-Sleep -Seconds $PollSeconds
        } elseif ($run.status -eq 'completed' -and $run.conclusion -eq 'success') {
            Write-Host 'The previous workflow succeeded but the release is incomplete; dispatching a repair run.'
            Invoke-Native gh @('workflow', 'run', $ReleaseWorkflow, '--ref', $Tag, '-f', "tag=$Tag")
            Start-Sleep -Seconds $PollSeconds
        }
    }

    return Wait-ForWorkflowRun -Workflow $ReleaseWorkflow -CommitSha $CommitSha -Deadline $Deadline
}

function Assert-GitHubRelease {
    param([Parameter(Mandatory)][string] $Tag)

    $json = Invoke-Native gh @(
        'release', 'view', $Tag,
        '--json', 'tagName,isDraft,isPrerelease,url,targetCommitish,assets'
    ) -Capture
    $release = $json | ConvertFrom-Json
    if ($release.tagName -ne $Tag) { throw "Release tag is '$($release.tagName)', expected '$Tag'." }
    if ($release.isDraft) { throw "Release $Tag is still a draft." }
    if ($release.isPrerelease) { throw "Release $Tag is marked as a prerelease." }

    $requiredAssets = @(
        "Dropwheel-$Tag-win-x64.zip",
        "Dropwheel-$Tag-win-x64-self-contained.zip",
        "Dropwheel-$Tag-SHA256SUMS.txt"
    )
    foreach ($name in $requiredAssets) {
        $asset = @($release.assets | Where-Object { $_.name -eq $name })
        if ($asset.Count -ne 1) { throw "Release asset '$name' is missing or duplicated." }
        if ([int64] $asset[0].size -le 0) { throw "Release asset '$name' is empty." }
    }
    return $release
}

function Remove-SafeTemporaryDirectory {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove non-temporary path '$resolved'."
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Sync-LocalReleaseBranch {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $Branch,
        [Parameter(Mandatory)][string] $ReleaseSha
    )

    # The release commit is built and pushed from an isolated worktree, so the working checkout's branch
    # still lags origin by that commit. Fast-forward it now - only when it is safe - so the next push
    # stays a fast-forward instead of being rejected. Any obstacle just prints guidance; the release is
    # already done, so this must never fail the run.
    Push-Location $RepoRoot
    try {
        $current = (& git rev-parse --abbrev-ref HEAD 2>$null)
        if ($LASTEXITCODE -ne 0 -or $current.Trim() -ne $Branch) {
            Write-Host "Local checkout is not on '$Branch' - run 'git pull --ff-only' there when convenient."
            return
        }
        if (& git status --porcelain) {
            Write-Warning "Local '$Branch' has uncommitted changes - not fast-forwarding. Run 'git pull --ff-only' when clean."
            return
        }
        & git merge-base --is-ancestor HEAD $ReleaseSha 2>$null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Local '$Branch' is not behind the release commit - nothing to fast-forward."
            return
        }
        & git merge --ff-only $ReleaseSha 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Local '$Branch' fast-forwarded to the release commit."
        } else {
            Write-Warning "Could not fast-forward local '$Branch'. Run 'git pull --ff-only' manually."
        }
    } finally {
        Pop-Location
    }
}

foreach ($tool in @('git', 'dotnet', 'gh')) {
    Assert-Tool $tool
}

$repoRoot = Invoke-Native git @('rev-parse', '--show-toplevel') -Capture
$originalLocation = Get-Location
$worktreePath = $null
$publishPath = $null
$deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)

try {
    Set-Location $repoRoot
    Invoke-Native gh @('auth', 'status')
    Invoke-Native git @('fetch', $Remote, $Branch, '--tags', '--prune')

    $mainRef = "$Remote/$Branch"
    $baseSha = Invoke-Native git @('rev-parse', $mainRef) -Capture
    $currentVersion = Get-ProjectVersionAtRef $mainRef
    $targetVersion = Get-NextVersion -Current $currentVersion -RequestedBump $Bump
    $previousTag = "v$currentVersion"
    $targetTag = "v$targetVersion"

    Write-Host "Remote base: $mainRef at $baseSha"
    Write-Host "Version: $currentVersion -> $targetVersion"

    if (-not (Test-GitRef "refs/tags/$previousTag")) {
        throw "Current project version $currentVersion has no local/fetched tag $previousTag."
    }
    if (-not (Test-RemoteTag -RemoteName $Remote -Tag $previousTag)) {
        throw "Current project version $currentVersion has no remote tag $previousTag on $Remote."
    }
    $previousTagSha = Invoke-Native git @('rev-list', '-n', '1', "refs/tags/$previousTag") -Capture
    if (-not (Test-GitAncestor -Ancestor $previousTagSha -Descendant $mainRef)) {
        throw "Current version tag $previousTag is not an ancestor of $mainRef."
    }

    $isResume = $targetVersion -eq $currentVersion
    if ($isResume -and $Bump -notmatch $SemVerPattern) {
        throw 'A computed bump cannot equal the current version.'
    }

    if ($isResume) {
        $releaseSha = Get-ReleaseCommit -MainRef $mainRef -Version $targetVersion
        if ((Get-ProjectVersionAtRef $releaseSha) -ne $targetVersion) {
            throw "Release commit $releaseSha does not contain version $targetVersion."
        }
        Write-Host "Resuming release $targetTag from commit $releaseSha."
    } else {
        if ((Compare-SemVer -Left $targetVersion -Right $currentVersion) -le 0) {
            throw "Target version $targetVersion must be greater than $currentVersion."
        }
        if (Test-GitRef "refs/tags/$targetTag") {
            throw "Tag $targetTag already exists locally or was fetched from $Remote."
        }
        if (Test-RemoteTag -RemoteName $Remote -Tag $targetTag) {
            throw "Tag $targetTag already exists on $Remote."
        }
        if (Test-GitHubReleaseExists $targetTag) {
            throw "GitHub Release $targetTag already exists."
        }

        $repository = Invoke-Native gh @('repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner') -Capture
        $worktreePath = Join-Path ([IO.Path]::GetTempPath()) "dropwheel-release-$targetVersion-$PID"
        if (Test-Path -LiteralPath $worktreePath) {
            throw "Temporary worktree path already exists: $worktreePath"
        }
        Invoke-Native git @('worktree', 'add', '--detach', $worktreePath, $baseSha)
        Set-Location $worktreePath

        $projectFullPath = Join-Path $worktreePath $ProjectPath
        $projectText = [IO.File]::ReadAllText($projectFullPath)
        $oldElement = "<Version>$currentVersion</Version>"
        if ([regex]::Matches($projectText, [regex]::Escape($oldElement)).Count -ne 1) {
            throw "Expected exactly one '$oldElement' element in $ProjectPath."
        }
        $projectText = $projectText.Replace($oldElement, "<Version>$targetVersion</Version>")
        [IO.File]::WriteAllText($projectFullPath, $projectText, [Text.UTF8Encoding]::new($false))

        $changelogFullPath = Join-Path $worktreePath $ChangelogPath
        $changelog = [IO.File]::ReadAllText($changelogFullPath)
        $newLine = if ($changelog.Contains("`r`n")) { "`r`n" } else { "`n" }
        $section = New-ChangelogSection -PreviousTag $previousTag -TargetTag $targetTag -BaseSha $baseSha -Repository $repository -NewLine $newLine
        $headingPattern = '\A# Changelog\r?\n\r?\n'
        if (-not [regex]::IsMatch($changelog, $headingPattern)) {
            throw 'CHANGELOG.md does not start with the expected heading.'
        }
        $changelog = [regex]::Replace(
            $changelog,
            $headingPattern,
            [Text.RegularExpressions.MatchEvaluator] { param($match) "# Changelog$newLine$newLine$section$newLine$newLine" },
            1
        )
        [IO.File]::WriteAllText($changelogFullPath, $changelog, [Text.UTF8Encoding]::new($false))

        Assert-ReleaseFileAllowlist

        $publishPath = Join-Path ([IO.Path]::GetTempPath()) "dropwheel-publish-$targetVersion-$PID"
        if (Test-Path -LiteralPath $publishPath) {
            throw "Temporary publish path already exists: $publishPath"
        }
        [void] (New-Item -ItemType Directory -Path $publishPath)
        Invoke-Native dotnet @('test', 'tests/Dropwheel.Tests/Dropwheel.Tests.csproj', '--nologo', '-c', 'Release')
        Invoke-Native dotnet @('publish', 'src/Dropwheel', '-c', 'Release', '-o', (Join-Path $publishPath 'fd'))
        Invoke-Native dotnet @(
            'publish', 'src/Dropwheel', '-c', 'Release', '-r', 'win-x64',
            '--self-contained', 'true', '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-o', (Join-Path $publishPath 'sc')
        )
        foreach ($expectedExe in @(
            (Join-Path $publishPath 'fd/Dropwheel.exe'),
            (Join-Path $publishPath 'sc/Dropwheel.exe')
        )) {
            if (-not (Test-Path -LiteralPath $expectedExe)) {
                throw "Publish smoke check did not produce '$expectedExe'."
            }
        }
        Assert-ReleaseFileAllowlist

        if ($DryRun) {
            Write-Host "Dry run passed. No commit, push, tag, or release was created for $targetTag."
            exit 0
        }

        Invoke-Native git @('add', '--', $ChangelogPath, $ProjectPath)
        Invoke-Native git @('diff', '--cached', '--check')
        Invoke-Native git @('commit', '-m', "chore(release): Dropwheel $targetVersion")
        $releaseSha = Invoke-Native git @('rev-parse', 'HEAD') -Capture
        Invoke-Native git @('push', $Remote, "HEAD:$Branch")
        Write-Host "Release commit pushed: $releaseSha"
    }

    if ($DryRun) {
        Write-Host 'DryRun has no work to resume.'
        exit 0
    }

    Set-Location $repoRoot
    [void] (Wait-ForWorkflowRun -Workflow $CiWorkflow -CommitSha $releaseSha -Deadline $deadline)
    Invoke-Native git @('fetch', $Remote, $Branch, '--tags', '--prune')
    if (-not (Test-GitAncestor -Ancestor $releaseSha -Descendant "$Remote/$Branch")) {
        throw "Release commit $releaseSha is no longer an ancestor of $Remote/$Branch."
    }

    $localTagExists = Test-GitRef "refs/tags/$targetTag"
    $remoteTagExists = Test-RemoteTag -RemoteName $Remote -Tag $targetTag
    if ($remoteTagExists -and -not $localTagExists) {
        Invoke-Native git @('fetch', $Remote, "refs/tags/${targetTag}:refs/tags/$targetTag")
        $localTagExists = $true
    }

    if ($localTagExists) {
        $tagSha = Invoke-Native git @('rev-list', '-n', '1', "refs/tags/$targetTag") -Capture
        if ($tagSha -ne $releaseSha) {
            throw "Existing tag $targetTag points to $tagSha, expected $releaseSha."
        }
    } else {
        Invoke-Native git @('tag', '-a', $targetTag, $releaseSha, '-m', "Dropwheel $targetVersion")
        $localTagExists = $true
    }

    $tagWasPushed = $false
    if (-not $remoteTagExists) {
        Invoke-Native git @('push', $Remote, "refs/tags/$targetTag")
        $tagWasPushed = $true
        Write-Host "Annotated tag pushed: $targetTag"
    } else {
        Write-Host "Tag $targetTag already points to the release commit on $Remote."
    }

    if (-not (Test-GitHubReleaseExists $targetTag)) {
        if ($tagWasPushed) {
            [void] (Wait-ForWorkflowRun -Workflow $ReleaseWorkflow -CommitSha $releaseSha -Deadline $deadline)
        } else {
            [void] (Start-OrResumeReleaseWorkflow -Tag $targetTag -CommitSha $releaseSha -Deadline $deadline)
        }
    }
    $release = Assert-GitHubRelease $targetTag

    Write-Host ''
    Write-Host "Release complete: $currentVersion -> $targetVersion"
    Write-Host "Commit: $releaseSha"
    Write-Host "Tag: $targetTag"
    Write-Host "URL: $($release.url)"
    foreach ($asset in $release.assets | Sort-Object name) {
        Write-Host "Asset: $($asset.name) ($($asset.size) bytes)"
    }

    Sync-LocalReleaseBranch -RepoRoot $repoRoot -Branch $Branch -ReleaseSha $releaseSha
} finally {
    Set-Location $originalLocation
    if ($publishPath) {
        Remove-SafeTemporaryDirectory $publishPath
    }
    if ($worktreePath -and (Test-Path -LiteralPath $worktreePath)) {
        Set-Location $repoRoot
        & git worktree remove --force $worktreePath
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Could not remove temporary worktree: $worktreePath"
        }
        Set-Location $originalLocation
    }
}
