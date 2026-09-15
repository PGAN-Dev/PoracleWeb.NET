# CI/CD

## Branches

| Branch | Purpose |
|---|---|
| `main` | Released code. Only moves when a release is merged. Publishing a release produces `:latest`, which self-hosters running watchtower auto-deploy. A plain `git clone` lands here, so cloning gives you released code. |
| `develop` | Integration. **Open pull requests against this.** Publishes `:beta` on every merge. |

Cutting a release means merging `develop` into `main` and then publishing a GitHub release — the release
is what triggers the `:latest` build, so the merge alone ships nothing. `release-changelog.yml` opens a
PR promoting `[Unreleased]` to the new version section.

`ci.yml` and `changelog.yml` run for pushes and pull requests on **both** branches. That matters: a
workflow filtered to one branch means PRs into the other run with no checks at all and merge looking
green.

`main` is behind a merge queue, so `ci.yml` and `changelog.yml` also list the `merge_group` trigger.
Required checks are reported against the queue's temporary merge branch rather than the PR head;
without that trigger a queued PR waits on checks that never run and times out.

## ci.yml

Runs on every push, PR and merge-queue entry for `main` and `develop`, as two independent jobs:

1. **Backend (.NET)** — restore, `dotnet build --configuration Release`, `dotnet test`, and upload the
   `.trx` results as an artifact.
2. **Frontend (Angular)** — `npm ci` on Node 22, then ESLint, the Prettier check, Jest (`npm test -- --ci`)
   and a production `npm run build`.

The frontend job pins npm 11 before installing. Node 22 ships npm 10.9.7, whose `npm ci` rejects
lockfiles that omit nested optional-peer entries; npm 11 is what Dependabot uses to regenerate them.

## docker-publish.yml

Runs when a **GitHub release is published**, on every push to `develop`, and on manual dispatch — *not*
on push to `main`. Publishing the release is what produces `:latest`, so merging `develop` into `main`
alone ships nothing.

| Trigger | Tags produced |
|---|---|
| Release published | `:latest`, `:X.Y.Z`, `:X.Y`, `:<sha>` — note `docker/metadata-action` strips the leading `v`, so a `v2.14.0` tag publishes `:2.14.0` |
| Push to `develop` | `:beta`, `:develop-<sha>` |
| Manual dispatch | None — every `enable=` condition is false and the semver patterns need a tag ref, so no tags are emitted |

Version, commit SHA and build timestamp are passed in as build args so `GET /api/version` can report
them from inside the running container. OCI labels alone are not readable at runtime.

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

Runs on every PR to `main` or `develop` as a **verify-only check** (it never writes to the repo). On a
merge-queue run it reports success without re-checking, because a `merge_group` payload carries no
pull request to read a title, label or diff from.

- Confirms the PR adds an entry under the `## [Unreleased]` section of `CHANGELOG.md`.
- **Exempt** PR types (no entry required): titles prefixed `deps:`, `docs:`, `style:`, `chore:`, `ci:`, `test:`, or `build:`. Conventional-commit scopes and `!` are allowed, so `fix(icons)!:` is matched the same way.
- **Escape hatch:** apply the `skip-changelog` label for a legitimate exception (re-runs automatically when the label is added). That label does not exist on the repository yet, so create it before you need it — you cannot apply one that has never been defined.
- Fails with a clear message if a user-facing PR is missing its `[Unreleased]` entry, so it's caught **before** merge.

!!! warning "Retitling a PR does not re-run the check"
    The trigger types are `opened`, `synchronize`, `reopened`, `labeled` and `unlabeled` — `edited` is
    not among them. Renaming a PR to an exempt prefix leaves the failed run in place until you push a
    commit or add a label.

> Maintain `CHANGELOG.md` manually in each PR using the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format — add your entry under `## [Unreleased]` (e.g. beneath `### Added` / `### Fixed`).

## release-changelog.yml

Runs when a GitHub release is published. It works on `main` and opens a PR rather than pushing, so it
cannot trip branch protection:

- Promotes `[Unreleased]` to `[X.Y.Z] - <date>`, opens a fresh `[Unreleased]`, and rewrites the
  comparison links.
- Appends a `### Dependencies` section listing every `deps:` commit merged since the previous tag.
  **Do not hand-write those entries** — Dependabot PRs are exempt from the changelog check precisely
  so they can be batched here, and a manual entry will be duplicated.
- Opens the PR, approves it and enables auto-merge.

The approve-and-merge half needs the `CHANGELOG_APP_ID` and `CHANGELOG_APP_PRIVATE_KEY` secrets. Without
them the PR is authored by `GITHUB_TOKEN`, which does not trigger the required checks, so it opens with
checks permanently pending and needs an admin merge. The workflow logs a warning saying so.

## docs.yml

Publishes this site to GitHub Pages with `mkdocs gh-deploy`, on pushes to `main` that touch `docs/**` or
`mkdocs.yml`, on every published release, and on manual dispatch. The release trigger exists so the
version badge — fetched from the GitHub Releases API at build time — stays current when a release
changes no documentation.

## Supporting workflows

`docker-preview.yml` builds a `:pr-<number>` image for any PR carrying the `preview` label and comments
the pull command on the PR. The tag rebuilds on every subsequent push.

`docker-prune.yml` runs daily at 03:00 UTC, deleting `pr-*` tags for closed PRs and keeping only the
last ten `develop-<sha>` images.

`pr-labeler.yml` applies a type label from the branch prefix, falling back to the PR title prefix.

`auto-merge-deps.yml` enables auto-merge on Dependabot PRs for patch bumps, curated groups and GitHub
Actions minor bumps. Majors and runtime-dependency minors wait for a human.
