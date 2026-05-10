#!/usr/bin/env bash
# Release helper for NativeBeam.
#
# Verifies the working tree is on `main`, runs `dotnet test`, rewrites the
# <Version> tag in Directory.Build.props, commits the bump, tags it as
# v<version>, and pushes both the commit and the tag to `origin`.
#
# Usage:
#   scripts/release.sh <version> [--remote <name>] [--skip-tests]
#
# Examples:
#   scripts/release.sh 0.2.0
#   scripts/release.sh 0.3.0-rc.1 --remote upstream

set -Eeuo pipefail

VERSION=""
REMOTE="origin"
SKIP_TESTS=0

usage() {
    sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'
    exit "${1:-1}"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -h|--help)
            usage 0
            ;;
        --remote)
            [[ $# -ge 2 ]] || { echo "--remote requires an argument" >&2; exit 1; }
            REMOTE="$2"
            shift 2
            ;;
        --skip-tests)
            SKIP_TESTS=1
            shift
            ;;
        -*)
            echo "Unknown option: $1" >&2
            usage 1
            ;;
        *)
            if [[ -n "$VERSION" ]]; then
                echo "Unexpected positional argument: $1" >&2
                usage 1
            fi
            VERSION="$1"
            shift
            ;;
    esac
done

if [[ -z "$VERSION" ]]; then
    echo "error: <version> is required" >&2
    usage 1
fi

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
    echo "error: '$VERSION' is not a valid semantic version (e.g. 0.2.0 or 0.3.0-rc.1)" >&2
    exit 1
fi

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
PROPS_PATH="$REPO_ROOT/Directory.Build.props"
TAG="v$VERSION"

step() {
    printf '\033[36m==> %s\033[0m\n' "$*"
}

cd "$REPO_ROOT"

step "Verifying clean working tree on 'main'"
BRANCH="$(git rev-parse --abbrev-ref HEAD)"
if [[ "$BRANCH" != "main" ]]; then
    echo "error: releases must be cut from 'main'; current branch is '$BRANCH'." >&2
    exit 1
fi

if [[ -n "$(git status --porcelain)" ]]; then
    echo "error: working tree is dirty. Commit or stash before releasing." >&2
    git status --short >&2
    exit 1
fi

step "Verifying tag $TAG does not already exist"
if git rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then
    echo "error: tag '$TAG' already exists locally." >&2
    exit 1
fi

if [[ "$SKIP_TESTS" -eq 0 ]]; then
    step "Running dotnet test"
    dotnet test --nologo
else
    echo "warning: skipping tests (--skip-tests)" >&2
fi

step "Rewriting <Version> in Directory.Build.props -> $VERSION"
if [[ ! -f "$PROPS_PATH" ]]; then
    echo "error: $PROPS_PATH not found." >&2
    exit 1
fi
if ! grep -q '<Version>[^<]*</Version>' "$PROPS_PATH"; then
    echo "error: no <Version>...</Version> element found in $PROPS_PATH." >&2
    exit 1
fi
# In-place rewrite using a portable temp file (BSD/GNU sed both work).
TMP="$(mktemp)"
trap 'rm -f "$TMP"' EXIT
sed "s|<Version>[^<]*</Version>|<Version>$VERSION</Version>|" "$PROPS_PATH" > "$TMP"
mv "$TMP" "$PROPS_PATH"
trap - EXIT

step "Committing and tagging"
git add -- "$PROPS_PATH"
if git diff --cached --quiet; then
    echo "warning: Directory.Build.props already at $VERSION; skipping commit."
else
    git commit -m "chore: bump version to $VERSION"
fi
git tag -a "$TAG" -m "Release $VERSION"

step "Pushing to '$REMOTE'"
git push "$REMOTE" main
git push "$REMOTE" "$TAG"

printf '\033[32mReleased %s (tag %s).\033[0m\n' "$VERSION" "$TAG"
