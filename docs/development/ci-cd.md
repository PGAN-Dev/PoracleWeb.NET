# CI/CD

Two GitHub Actions workflows run on push to `main` and pull requests.

## ci.yml

Runs on every push and PR:

1. **Backend** — Build .NET 10 solution, run xUnit tests
2. **Frontend** — Install dependencies, run ESLint, run Prettier check, run Jest tests, build Angular

## docker-publish.yml

Runs on push to `main`:

1. Builds the Docker image
2. Publishes to [`ghcr.io/pgan-dev/poracleweb.net`](https://github.com/PGAN-Dev/PoracleWeb.NET/pkgs/container/poracleweb.net)
3. Tags with `latest` and commit SHA

## changelog.yml

Runs on every PR to `main` as a **verify-only check** (it never writes to the repo):

- Confirms the PR adds an entry under the `## [Unreleased]` section of `CHANGELOG.md`.
- **Exempt** PR types (no entry required): titles prefixed `docs:`, `style:`, `chore:`, `ci:`, `test:`, or `build:`.
- **Escape hatch:** apply the `skip-changelog` label for a legitimate exception (re-runs automatically when the label is added).
- Fails with a clear message if a user-facing PR is missing its `[Unreleased]` entry, so it's caught **before** merge.

> Maintain `CHANGELOG.md` manually in each PR using the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format — add your entry under `## [Unreleased]` (e.g. beneath `### Added` / `### Fixed`).

## release-changelog.yml

Runs on GitHub release events:

- Converts the `[Unreleased]` section to a versioned section with date
- Updates comparison links
