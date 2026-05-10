#requires -Version 5.1
<#
.SYNOPSIS
  Release helper for NativeBeam.

.DESCRIPTION
  Enforces the trunk-based + release-branch workflow documented in
  docs/BRANCHING.md:

    - Stable versions (e.g. 0.1.0, 0.1.1) MUST be cut from the matching
      `release/MAJOR.MINOR.x` branch — one branch per minor line that hosts
      every patch in that line.
    - Prereleases (e.g. 0.2.0-rc.1) may be cut from `master`, any
      `release/MAJOR.MINOR.x`, or any `feature/*` / `fix/*` / `perf/*` /
      `chore/*` / `ci/*` / `docs/*` branch.

  Steps: branch check, clean-tree check, tag pre-check, `dotnet test`,
  in-place <Version> rewrite, commit, annotated tag, push current branch +
  tag, then attempt to open a backmerge PR via the GitHub CLI.

.PARAMETER Version
  Semantic version, e.g. 0.2.0 (no leading 'v').

.PARAMETER Remote
  Git remote to push to. Defaults to 'origin'.

.PARAMETER SkipTests
  Skip `dotnet test`. Use only for emergency releases where tests are known
  green from a prior CI run.

.PARAMETER NoPr
  Skip the automatic backmerge PR creation.

.EXAMPLE
  ./scripts/release.ps1 -Version 0.3.0-alpha.1
  ./scripts/release.ps1 -Version 0.1.0          # must be on release/0.1.x
  ./scripts/release.ps1 -Version 0.1.1          # also on release/0.1.x
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$Remote = 'origin',

    [switch]$SkipTests,

    [switch]$NoPr
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$tag = "v$Version"
$isPrerelease = $Version -match '-'

function Step([string]$message) {
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Invoke-Native {
    param([Parameter(Mandatory)][string]$File, [string[]]$Args)
    & $File @Args
    if ($LASTEXITCODE -ne 0) {
        throw "$File $($Args -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repoRoot
try {
    Step "Verifying current branch matches version policy"
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    $versionMajorMinor = ($Version -split '[-+]')[0] -replace '^(\d+\.\d+)\..*$', '$1'
    $expectedReleaseBranch = "release/$versionMajorMinor.x"
    $releaseBranchPattern = '^release/\d+\.\d+\.x$'

    if ($isPrerelease) {
        $isAllowed = $branch -eq 'master' `
            -or $branch -match $releaseBranchPattern `
            -or $branch -match '^(feature|fix|perf|chore|ci|docs)/'
        if (-not $isAllowed) {
            throw "Prerelease '$Version' must be cut from master, release/MAJOR.MINOR.x, or a work branch (feature/*, fix/*, perf/*, chore/*, ci/*, docs/*); current branch is '$branch'."
        }
    } else {
        if ($branch -notmatch $releaseBranchPattern) {
            throw "Stable version '$Version' must be cut from a 'release/MAJOR.MINOR.x' branch (got '$branch'). See docs/BRANCHING.md."
        }
        if ($branch -ne $expectedReleaseBranch) {
            throw "Stable version '$Version' belongs on '$expectedReleaseBranch'; current branch is '$branch'."
        }
    }

    Step "Verifying clean working tree"
    $status = (git status --porcelain)
    if ($status) {
        throw "Working tree is dirty. Commit or stash before releasing:`n$status"
    }

    Step "Verifying tag $tag does not already exist"
    $existing = git tag --list $tag
    if ($existing) {
        throw "Tag '$tag' already exists locally. Delete it or pick a new version."
    }

    if (-not $SkipTests) {
        Step "Running dotnet test"
        Invoke-Native -File 'dotnet' -Args @('test', '--nologo')
    } else {
        Write-Warning "Skipping tests (-SkipTests)."
    }

    Step "Rewriting <Version> in Directory.Build.props -> $Version"
    if (-not (Test-Path $propsPath)) {
        throw "Cannot find $propsPath."
    }
    $content = Get-Content -LiteralPath $propsPath -Raw
    $pattern = '<Version>[^<]*</Version>'
    if ($content -notmatch $pattern) {
        throw "Could not locate a <Version>...</Version> element in $propsPath."
    }
    $updated = [regex]::Replace($content, $pattern, "<Version>$Version</Version>", 1)
    if ($updated -eq $content) {
        Write-Warning "Version already at $Version; no rewrite needed."
    } else {
        Set-Content -LiteralPath $propsPath -Value $updated -NoNewline -Encoding utf8
    }

    Step "Committing and tagging"
    Invoke-Native -File 'git' -Args @('add', '--', $propsPath)
    Invoke-Native -File 'git' -Args @('commit', '-m', "chore: bump version to $Version")
    Invoke-Native -File 'git' -Args @('tag', '-a', $tag, '-m', "Release $Version")

    Step "Pushing branch '$branch' and tag '$tag' to '$Remote'"
    Invoke-Native -File 'git' -Args @('push', $Remote, $branch)
    Invoke-Native -File 'git' -Args @('push', $Remote, $tag)

    if (-not $NoPr -and $branch -match $releaseBranchPattern) {
        $gh = Get-Command gh -ErrorAction SilentlyContinue
        if ($null -ne $gh) {
            Step "Opening backmerge PR ($branch -> master)"
            try {
                Invoke-Native -File 'gh' -Args @(
                    'pr', 'create',
                    '--base', 'master',
                    '--head', $branch,
                    '--title', "chore: backmerge $tag",
                    '--body', "Backmerges the ``$tag`` release into ``master``. Auto-generated by ``scripts/release.ps1``."
                )
            } catch {
                Write-Warning "'gh pr create' failed; open the PR manually."
            }
        } else {
            Write-Host "note: GitHub CLI ('gh') not found. Open the backmerge PR manually:" -ForegroundColor Yellow
            Write-Host "      gh pr create --base master --head $branch --title 'chore: backmerge $tag'" -ForegroundColor Yellow
        }
    }

    Write-Host "Released $Version (tag $tag) from $branch." -ForegroundColor Green
}
finally {
    Pop-Location
}
