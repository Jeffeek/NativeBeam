# Branching strategy

NativeBeam uses **trunk-based development with short-lived release branches**.
The model is small on purpose: one always-stable trunk, one branch type for
work, one branch type for stabilisation.

## Branches

### `master`

The trunk. Always green.

- **Direct push is forbidden.** Configure branch protection: require PR review,
  require CI green, require linear history.
- Every commit on `master` should ship-ready: it has passed CI on Linux and
  Windows, it is AOT-clean, and it does not reduce coverage below the codecov
  gate.
- Tags for prereleases (`v*-alpha.*`, `v*-beta.*`, `v*-rc.*`) may be cut from
  `master` directly — see [`scripts/release.{sh,ps1}`](../scripts/).

### `feature/<short-name>`

Short-lived work branches.

- Branch off `master`. Rebase on `master`, do not merge.
- One logical change per branch. Bundle pure refactors separately from
  behaviour changes.
- Naming: `feature/header-templates`, `feature/cdp-network-events`. Use
  `fix/`, `perf/`, `chore/`, `ci/`, `docs/` prefixes when those types are a
  better fit — same lifecycle, different conventional-commit type.
- Open a PR into `master`. CI gates: `ci.yml`, `dependency-review.yml`,
  `codeql.yml`. PR is squashed (or rebase-merged) on green.

### `release/<MAJOR>.<MINOR>.x`

Long-lived stabilisation branches — **one per minor line**, hosting every
patch in that line.

- Created from `master` when we are ready to start hardening a minor.
  Naming examples: `release/0.1.x`, `release/1.0.x`. The literal `.x` makes
  the role obvious: this is the branch for every patch on the `0.1` line,
  not for a single tag.
- **One branch per minor line** — `release/0.1.x` carries `v0.1.0`, then
  `v0.1.1`, then `v0.1.2`, etc. There is no `release/0.1.0` or
  `release/0.1.1`. The release script rejects a stable bump whose
  `MAJOR.MINOR` does not match the branch name.
- Only fixes flow into a release branch — bug fixes, documentation, build
  fixes, dependency bumps. **No new features.**
- Stable tags (`v0.1.0`, `v0.1.1`, `v1.0.0`, ...) are cut **only from the
  matching `release/MAJOR.MINOR.x` branch**.
- After every tag is pushed, a PR is opened from the release branch back
  into `master`. The branch is **kept alive** for future patches on the
  same minor line — do not delete it until the line is end-of-life.

## Versioning

[SemVer 2.0](https://semver.org/).

| Where | What can be tagged |
|---|---|
| `master` | `vMAJOR.MINOR.PATCH-alpha.N`, `-beta.N`, `-rc.N` |
| `release/MAJOR.MINOR.x` | `vMAJOR.MINOR.PATCH` (stable), `-rc.N` (release candidates) — only versions whose `MAJOR.MINOR` matches the branch |

Cutting a stable tag from `master` (or any feature branch) is a release-script
error. Cutting `0.2.0` from `release/0.1.x` is also a release-script error
— the script verifies the requested `MAJOR.MINOR` matches the branch.
Cutting a prerelease tag from a release branch is allowed; that is how
`-rc.N` candidates for the next patch are produced.

## Release flow

1. **Pick a version.** Decide whether it is a prerelease (continue from
   master) or a stable (cut a release branch).
2. **Stable cut (only when the minor line is new):**
   - `git switch -c release/0.2.x master`
   - `git push -u origin release/0.2.x`
   - If the line already exists (e.g. cutting `0.1.1` after `0.1.0`),
     skip this step and `git switch release/0.1.x` instead.
3. **Stabilise.** Land fixes via PRs targeting the release branch. CI gates
   apply.
4. **Tag.** Run `scripts/release.sh 0.2.0` (or `release.ps1`) on the release
   branch. The script:
   - Validates the branch matches the rule (release branch ⇄ stable
     version, AND `MAJOR.MINOR` of the version matches the branch name).
   - Runs the test suite.
   - Bumps `<Version>` in `Directory.Build.props`, commits, tags `v0.2.0`,
     pushes both.
   - Opens a PR from the release branch back to `master` (when the GitHub CLI
     is available); otherwise prints the manual `gh pr create` command.
5. **Backmerge.** Merge the release-branch PR into `master`. The
   `chore: bump version to …` commit and any release-only fixes flow back to
   trunk. **Keep the release branch alive** — the next patch on the same
   minor (e.g. `0.2.1`) will be cut from it.
6. **Publish.** The tag push triggers `publish.yml`, which runs the AOT
   gate, packs, and pushes to NuGet.org.

## Hotfixes

A hotfix on a shipped stable line is just a release-branch operation:

1. `git switch release/0.2.x`
2. Land the fix via PR (target the release branch, not `master`).
3. Run `scripts/release.sh 0.2.1`.
4. Backmerge to `master`.

The release branch stays put across patches — `release/0.2.x` carries
`v0.2.0`, `v0.2.1`, `v0.2.2`, ... If the branch was lost (e.g. accidentally
deleted), recreate it from the most recent matching tag:
`git switch -c release/0.2.x v0.2.2`.
