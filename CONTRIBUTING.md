# Contributing to NativeBeam

Thanks for considering a contribution. NativeBeam is a small, opinionated
library; the bar is high on AOT-cleanliness and zero-reflection guarantees, so
please read this before opening a PR.

## Ground rules

1. **Tests must pass locally.** Run `dotnet test` before pushing. The CI lane
   runs on `ubuntu-latest` and `windows-latest` against a real Chromium —
   skipped tests on your machine still need to be green there.
2. **No new reflection.** The whole point is AOT. If you need to (de)serialize
   something, add it to `CdpJsonContext` in
   `src/NativeBeam.Pdf/Cdp/CdpJsonContext.cs`. `Directory.Build.props`
   promotes `IL2026`/`IL3050`/etc. to errors — if your change introduces an
   AOT warning, the build fails.
3. **No new trim warnings.** Same posture as above.
4. **Public API changes need XML docs.** `<GenerateDocumentationFile>` is on;
   document the why, not the what.
5. **One logical change per PR.** Bundle pure refactors separately from
   behaviour changes so they can be reviewed independently.

## Branching

- Branch off `master`. Use a short, lowercase, hyphenated name —
  `feat/header-templates`, `fix/loadevent-race`, `chore/bump-xunit`.
- Rebase on `master` before opening the PR; do not merge `master` into your branch.

## Conventional commits

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope optional>): <imperative summary, lowercase, no period>

<optional body — wrap at 72>

<optional footer: BREAKING CHANGE, refs>
```

Allowed types:

| Type | Use for |
|---|---|
| `feat` | A user-visible feature or new public API |
| `fix` | A bug fix in user-visible behaviour |
| `perf` | A change whose only goal is performance |
| `refactor` | Internal restructuring, no behaviour change |
| `test` | Test-only changes |
| `docs` | README, XML docs, comments |
| `build` | csproj / SDK / package version changes |
| `ci` | Workflow changes |
| `chore` | Anything that doesn't fit the above (version bumps, license, etc.) |

Examples:

- `feat(pdf): expose header and footer templates`
- `fix(cdp): release subscriptions on websocket close`
- `perf(connection): pool the receive buffer with ArrayPool`

A PR that bumps a version is `chore: bump version to 0.3.0`, matching what
`scripts/release.{sh,ps1}` produces — if you cut a release manually, use the
script.

## Running the tests

```bash
dotnet test
```

The integration suite needs a Chromium-based browser. If yours lives outside
the well-known install paths, set `CHROME_PATH`:

```bash
CHROME_PATH=/opt/google/chrome/chrome dotnet test
```

If no browser is available the tests are **skipped** (via
`Xunit.SkippableFact`), not failed. CI fails them hard because Chrome is
provisioned by `browser-actions/setup-chrome`.

## Releasing

You don't need to. Maintainers cut releases via `scripts/release.{sh,ps1}`,
which handles the version bump, tag, and push. CI on tag push runs the AOT
publish gate before the NuGet push.
