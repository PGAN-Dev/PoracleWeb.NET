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

Runs on merged PRs:

- Extracts the PR title and categorizes using conventional commit prefixes (`feat`, `fix`, `refactor`, `docs`, etc.)
- Inserts the entry into the `[Unreleased]` section of `CHANGELOG.md`
- Commits the update automatically

## release-changelog.yml

Runs on GitHub release events:

- Converts the `[Unreleased]` section to a versioned section with date
- Updates comparison links
