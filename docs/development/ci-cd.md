# CI/CD

## Branches

| Branch | Purpose |
|---|---|
| `main` | Released code. Only moves when a release is merged. Publishing a release produces `:latest`, which self-hosters running watchtower auto-deploy. A plain `git clone` lands here, so cloning gives you released code. |
| `develop` | Integration. **Open pull requests against this.** Publishes `:beta` on every merge. |

Cutting a release means merging `develop` into `main` and then publishing a GitHub release — the release
is what triggers the `:latest` build, so the merge alone ships nothing. `release-changelog.yml` opens a
PR promoting `[Unreleased]` to the new version section.

GitHub Actions workflows run on pushes and pull requests for **both** branches. That matters: a workflow
filtered to one branch means PRs into the other run with no checks at all and merge looking green.

## ci.yml

Runs on every push and PR:

1. **Backend** — Build .NET 10 solution, run xUnit tests
2. **Frontend** — Install dependencies, run ESLint, run Prettier check, run Jest tests, build Angular

## docker-publish.yml

Runs when a **GitHub release is published**, on every push to `develop`, and on manual dispatch — *not*
on push to `main`. Publishing the release is what produces `:latest`, so merging `develop` into `main`
alone ships nothing.

| Trigger | Tags produced |
|---|---|
| Release published | `:latest`, `:X.Y.Z`, `:X.Y`, `:<sha>` — note `docker/metadata-action` strips the leading `v`, so a `v2.14.0` tag publishes `:2.14.0` |
| Push to `develop` | `:beta`, `:develop-<sha>` |
| Manual dispatch | None — every `enable=` condition is false and the semver patterns need a tag ref, so no tags are emitted |

Images go to [`ghcr.io/pgan-dev/poracleweb.net`](https://github.com/PGAN-Dev/PoracleWeb.NET/pkgs/container/poracleweb.net).

!!! info "How a release reaches production"
    Publishing the GitHub release is the moment production changes — merging `develop` into `main` on its
    own does nothing, because the image build is triggered by the `release` event.

    Deployment itself is by **watchtower**, which polls `:latest` every 60 seconds. The same applies to
    the dev instance, which polls `:beta` and therefore updates on every merge to `develop`.

    The workflow does contain an SSH deploy step (`docker compose pull && up -d --force-recreate` against
    the `DEPLOY_HOST` secret), but it is an **opt-in hook that is inert on this repository**: it exits 0
    early unless both `DEPLOY_HOST` and `DEPLOY_SSH_KEY` are set, and neither is. Self-hosters who prefer
    a push deploy to a polling agent can set them.

## changelog.yml

Runs on every PR to `main` or `develop` as a **verify-only check** (it never writes to the repo):

- Confirms the PR adds an entry under the `## [Unreleased]` section of `CHANGELOG.md`.
- **Exempt** PR types (no entry required): titles prefixed `deps:`, `docs:`, `style:`, `chore:`, `ci:`, `test:`, or `build:`.
- **Escape hatch:** apply the `skip-changelog` label for a legitimate exception (re-runs automatically when the label is added).
- Fails with a clear message if a user-facing PR is missing its `[Unreleased]` entry, so it's caught **before** merge.

> Maintain `CHANGELOG.md` manually in each PR using the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format — add your entry under `## [Unreleased]` (e.g. beneath `### Added` / `### Fixed`).

## release-changelog.yml

Runs on GitHub release events:

- Converts the `[Unreleased]` section to a versioned section with date
- Updates comparison links
