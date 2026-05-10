#requires -Version 5.1
<#
.SYNOPSIS
  Release helper for NativeBeam.

.DESCRIPTION
  Verifies the working tree is on `main`, runs `dotnet test`, rewrites the
  <Version> tag in Directory.Build.props, commits the bump, tags it as
  v<version>, and pushes both the commit and the tag to `origin`.

.PARAMETER Version
  Semantic version, e.g. 0.2.0 (no leading 'v').

.PARAMETER Remote
  Git remote to push to. Defaults to 'origin'.

.PARAMETER SkipTests
  Skip `dotnet test`. Use only for emergency releases where tests are known
  green from a prior CI run.

.EXAMPLE
  ./scripts/release.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$Remote = 'origin',

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$tag = "v$Version"

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
    Step "Verifying clean working tree on 'main'"
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -ne 'main') {
        throw "Releases must be cut from 'main'; current branch is '$branch'."
    }

    $status = (git status --porcelain)
    if ($status) {
        throw "Working tree is dirty. Commit or stash before releasing:`n$status"
    }

    Step "Verifying tag v$Version does not already exist"
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
        # Preserve original line endings.
        Set-Content -LiteralPath $propsPath -Value $updated -NoNewline -Encoding utf8
    }

    Step "Committing and tagging"
    Invoke-Native -File 'git' -Args @('add', '--', $propsPath)
    Invoke-Native -File 'git' -Args @('commit', '-m', "chore: bump version to $Version")
    Invoke-Native -File 'git' -Args @('tag', '-a', $tag, '-m', "Release $Version")

    Step "Pushing to '$Remote'"
    Invoke-Native -File 'git' -Args @('push', $Remote, 'main')
    Invoke-Native -File 'git' -Args @('push', $Remote, $tag)

    Write-Host "Released $Version (tag $tag)." -ForegroundColor Green
}
finally {
    Pop-Location
}
