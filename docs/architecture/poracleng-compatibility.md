# PoracleNG Version Compatibility

PoracleWeb.NET works against any PoracleNG from 5.1.0 upwards, and switches on the extra features of a
newer one when it finds them. There is nothing to configure.

The line that matters is **5.1.0 versus 5.2.0 and newer**. There was briefly a second question — whether
you were running PoracleNG's released `main` or its `develop` — and there no longer is: develop merged
and shipped. Anything written in terms of branches is out of date, including earlier drafts of
this page.

Every gate in this build compares against **5.2.0**, not against what production happens to run. 5.2.1 is
simply the first released build carrying those features, so a check written as `>= 5.2.1` would refuse a
5.2.0 server that can serve the request. `PoracleServerProfile.FirstWithV2Tracking`,
`MuteCapabilityService.MinimumVersion` and `QuestPokecoinCapabilityService.MinimumVersion` are all
`new Version(5, 2, 0)`.

Version support still matters, and always will, because self-hosters upgrade on their own schedule. The
PGAN production instance moved to 5.2.1 on 2026-08-24 (migrations 5 through 8 applied cleanly); a server
somewhere is still on 5.1.0, and one is on something older than that.

!!! warning "5.1.0 is the minimum"
    Below it, per-alarm delivery scope, the PVP mega evolution filter and the minimum time-left filter
    write columns that do not exist, so those three controls save without complaint and change nothing.
    PoracleWeb.NET logs an error at startup and shows the version on **Admin → Settings**.

## The v2 migration does not raise the floor

Moving to PoracleNG's `/api/v2` surface was the obvious moment to make 5.2.0 a hard minimum and delete
the v1 code paths. It was considered and refused, so here is the reasoning in one place rather than
scattered through commit messages.

Dropping v1 would cost a self-hoster on 5.1.0 their Areas page, their saved places, the location pin,
delegated-webhook resolution and all alarm editing. That is the application, not a feature.

What it would buy is smaller than it looks. Most of the movable v2 operations are renames with no
behaviour change at all. The genuine gains — the lure edit no longer having to delete and recreate the
row, an upstream 409 refusing an alarm collision instead of PoracleWeb reconstructing it from a 200 —
are workaround *removals*, and the fallback keeps them: the v2 path skips the workaround, the v1 path
keeps it. Nothing user-visible is unlocked by deleting v1.

Two things follow. Reads deliberately stay on v1, including the tempting full snapshot at
`GET /api/v2/humans/{id}/tracking`; the moment reads move, the internal currency stops being v1-shaped
and the fallback stops being free. And an operation that genuinely has no v1 equivalent —
`PUT /v2/humans/{id}/locations/{label}` is the only one — gets a capability gate and degrades to the
old flow, the pattern `MuteCapabilityService` already sets, rather than forcing a floor for one feature.

Two v2 surfaces cannot serve PoracleWeb at all yet: invasion, whose reads omit the targeting field for a
named grunt, and bulk distance, which has no batch write. So v1 has to stay in the codebase regardless.
Revisit the floor when upstream closes both — see
[the v2 findings](../poracleng-enhancement-requests.md#v2-findings-for-an-upstream-report).

## How support is decided

Three signals, probed together and cached for five minutes. The mechanics are in
[Backend Patterns](backend.md#server-capability-probe); what follows is how to choose between them.

| Signal | Source | Answers |
|---|---|---|
| Migration number | `schema_migrations` in the Poracle database | Whether a column exists. |
| Version | `GET /health` | Which release line. |
| Capability map | `GET /health` → `capabilities` | Whether a bot or template-editor feature is compiled in. A missing key means unsupported — PoracleNG's own contract for the map. |

**Every gate fails closed.** An unreachable server, an unread migration number and an unparseable
version all resolve to unsupported. Hiding a control that would have worked is a nuisance; showing one
that writes a column the server does not have is the silent failure this exists to prevent.

### Prefer the migration number

Where a column is what actually matters, gate on the schema and not on the release number. A version
string is a claim about a release line, not about this database, and the two come apart routinely:
self-hosters cherry-pick single commits, forks carry one feature and not another, and a locally built
binary reports `0.0.0` because the build flags were never injected. `PoracleServerProfile` already
treats `0.0.0` as unknown rather than ancient for that reason.

Gate on the version only when there is nothing better to gate on. Pokecoin quest rewards are the honest
example: PoracleNG widened an allowlist on `reward_type`. No column arrived, no migration ran, no
capability key appeared. The version is the only thing that changed, so the version is what decides.

## What depends on the server version

| Feature | Signal | Needs |
|---|---|---|
| Costume filter on pokemon alarms | Migration | PoracleNG database migration 6 |
| Costume filter on raid alarms | Migration | PoracleNG database migration 7 |
| Pokéstop-event tracking (`incident`) | v2 API surface | PoracleNG 5.2.0 |
| Mutes | v2 API surface | PoracleNG 5.2.0 |
| Pokecoin quest rewards (`reward_type: 8`) | Version | PoracleNG 5.2.0 |
| Pokemon edits through `/api/v2` | Version | PoracleNG 5.2.0, or `Poracle:TrackingApiVersion=v2` |
| Rule descriptions on alarm cards | Response field | v1 `allProfiles`, or any v2 read |

Each of these carries its own small capability service — `SummaryCapabilityService`,
`MuteCapabilityService`, `QuestPokecoinCapabilityService` and `CostumeCapabilityService` — all the same
shape over `IPoracleServerProfileService`: one method, one question, no cache of its own, since the
profile service already caches for five minutes and exposes `Invalidate()`. There is deliberately no
central registry: a registry
was written and abandoned, because the per-feature shape already existed and two mechanisms answering
one question is how one of them ends up being the one nobody updates.

Costume names are the awkward one. PoracleNG loads them into its game data under `costume_{id}` keys
but publishes them nowhere -- `/api/masterdata/` offers only `monsters` and `grunts` -- so the costume
picker reads the same WatWowMap masterfile PoracleNG itself reads. The names are therefore English in
every language, and a costume too new for that file shows as its number.

Costume values set elsewhere are already safe at every version. `TrackingFieldPreserver` and
`PoracleJsonHelper.RewriteRows` carry forward every stored field PoracleWeb.NET has no model for, so a
costume set with the bot survives a web edit on a server that has the column.

Rule descriptions belong on this list too. A v2 tracking read takes `?include_descriptions=true` and
returns a `description` on each rule -- the same sentence PoracleNG's bot answers a `!pokemon` command
with. It is easy to conclude otherwise: `description` is not on the named `V2PokemonRule` request
schema, and the response envelope that carries it is an inline object under `rules.items` rather than a
named component, so a search of `components/schemas` finds nothing. Resolve
`V2ListOutput...Body.properties.rules.items` before believing a field is absent.

The v1 `allProfiles` endpoint accepts the same `includeDescriptions` flag, so this one has a path on
both versions.

Reading and deleting an existing rule is **never** gated, only creating one. A rule can already exist —
set with the bot, or left behind by a downgrade — and a row nobody can see is a row nobody can delete.

## When a request cannot be served

`PoracleUnsupportedException` carries a plain feature name and what the server would need, in words. The
global `PoracleUnsupportedExceptionFilter` turns it into **409 Conflict**:

```json
{
  "error": "This PoracleNG does not support costume filters. It requires PoracleNG database migration 6.",
  "feature": "costume filters",
  "requires": "PoracleNG database migration 6"
}
```

409 rather than 403 on purpose. The 403 branch belongs to `disable_*` site settings and keys off a
`disableKey` in the body; borrowing it would put an operator's switch and a version shortfall behind the
same toast, and the version shortfall has the one detail worth reading in it. 409 has no interceptor
branch, so it falls through to the caller, which shows the message beside the control that caused it —
the same route `TrackingConflictExceptionFilter` already takes.

Throw it from the service layer, not from a controller. Quick-pick apply, profile duplicate and profile
import all reach the alarm services without passing an action that could have checked first.

## Adding a version-gated feature

1. **Pick the narrowest signal that predicts it**, per the rule above. Migration number if a column is
   involved, capability key if PoracleNG publishes one, version only as a last resort.
2. **Write a small per-feature service over `IPoracleServerProfileService`**, shaped like
   `ISummaryCapabilityService`: one method, one question, answering false when it cannot tell. There is
   no capability registry and there should not be one — a registry turns every feature into a key
   lookup against a table nobody reads, and the interesting part of each gate is the sentence
   explaining which signal was chosen and why.
3. **Guard the service write path**, on create, update and bulk alike, and throw
   `PoracleUnsupportedException(feature, requires)` with words the user can act on.
4. **Give the SPA a way to ask.** `GET /api/admin/server-profile` is admin-only, so it cannot be the
   answer for a user-facing control. Four ordinary authenticated endpoints answer instead, and one of
   them is the pattern to copy: `GET /api/summary-schedules/capability`,
   `GET /api/settings/costume-capability`, `GET /api/quests/capability`, and `GET /api/mutes`, which
   folds the capability into the list response rather than answering separately — the quiet chip needs
   both on every alarm page, so two calls would have been two calls every time.
5. **Prefer hiding the control to disabling it with an explanation.** There is nothing the user can do
   about their operator's PoracleNG version.
6. **Add a row to the table above.**

## Things that look like they differ between versions and do not

These are the ones most likely to send you writing a check that buys nothing.

**The v1 API is frozen, and unchanged on 5.2.1.** Its release notes describe RFC 9457
`application/problem+json` error bodies and 422 status codes, and those are real — on `/api/v2` only.
Verified by calling both surfaces on the same 5.2.1 server: a v1 tracking POST for a user that does not
exist answers `{"message":"User not found","status":"error"}`, byte-identical to 5.1.0, while a
malformed v2 path parameter answers 422 with `Content-Type: application/problem+json` and an `errors`
array. Assuming the new shapes applied everywhere cost a wrongly framed issue and pull request.

Because v1 did not move, almost all of PoracleWeb.NET stays on it: every read, every create, both
distance endpoints and nine of the ten tracking types. Three things speak v2 — pokemon updates, mutes
and Pokéstop events — and each reads errors through `PoracleProblemDetails`, which handles both
dialects. See [PoracleNG API Proxy](poracleng-proxy.md#the-v2-pilot).

**Do not take `active_hours` day numbering from PoracleNG's OpenAPI schema.** `V2ActiveHourEntry.day` is
declared `minimum: 0, maximum: 6` and described as "0=Sunday … 6=Saturday". The scheduler uses ISO
weekdays, Monday 1 through Sunday 7 (`isoDow` in `processor/cmd/processor/profiles.go`). The schema
therefore rejects Sunday and accepts a meaningless 0. PoracleWeb.NET validates 1 to 7, which is correct;
`ProfileController.ValidateActiveHours` is the place that would be wrong if somebody "fixed" it against
the document.

**The `/health` capability map is not where alarm features appear.** Between 5.1.0 and 5.2.1 it gained
exactly one key, `derivedDtsTypes`, and says nothing about costume, incidents or mutes. It covers bot and
template-editor features; the migration number is what covers columns. Reaching for `Supports()` when
`HasSchema()` is the question will quietly answer false forever.
