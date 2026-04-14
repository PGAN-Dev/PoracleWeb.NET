# Testing Pre-release Builds

PoracleWeb.NET publishes three Docker image channels on GHCR. Pick one based on how much risk you're willing to accept.

| Channel | Tag | What it is | Updates |
|---|---|---|---|
| **Stable** | `:latest`, `:v2.5.0` | Tagged releases. Battle-tested. | On release |
| **Beta** | `:beta` | Latest `main`. Next release candidate. | Every merge to main |
| **PR preview** | `:pr-123` | A specific pull request. Unreviewed code. | Every push to that PR |

Image registry: [`ghcr.io/pgan-dev/poracleweb.net`](https://github.com/PGAN-Dev/PoracleWeb.NET/pkgs/container/poracleweb.net).

---

## Switching channels

Edit your local `docker-compose.yml` (copied from `docker-compose.yml.example` during setup):

```yaml
services:
  poracleweb:
    image: ghcr.io/pgan-dev/poracleweb.net:beta   # or :latest, or :pr-123
```

Then:

```bash
docker compose pull
docker compose up -d --force-recreate
```

To roll back, change the tag back to `:latest` and repeat.

---

## Trying a specific PR

1. Find the PR on GitHub. If you see a `preview` label and a bot comment with a `pr-N` tag, an image exists.
2. If no image exists, ask the maintainer to apply the `preview` label — CI will build one.
3. Switch your compose file to `:pr-<number>` and redeploy as above.
4. Report findings (good or bad) directly on the PR.

The tag updates automatically on every new push to the PR, so `docker compose pull` always gets the latest fix.

---

## Backing up before testing

Pre-release builds may include schema migrations that are hard to reverse. Before switching off `:latest`:

```bash
# dump poracle_web DB
mysqldump -h <host> -u <user> -p poracle_web > poracle_web-backup.sql

# snapshot your .env and compose file
cp .env .env.bak
cp docker-compose.yml docker-compose.yml.bak
```

If things go sideways, restore the DB and revert the image tag.

---

## Building from source instead

If you'd rather compile a branch locally (no GHCR pull):

```bash
git fetch origin pull/<N>/head:pr-<N>
git checkout pr-<N>
./scripts/docker.sh build
docker compose up -d --force-recreate
```

---

## Reporting issues

- **On a PR preview**: comment directly on the PR.
- **On `:beta`**: open a GitHub issue and mention the `main-<sha>` tag you're running (`docker inspect` the container to find it).
- **On `:latest`**: open a GitHub issue with the version tag.

Include: your channel/tag, `docker compose logs --tail 200`, steps to reproduce.
