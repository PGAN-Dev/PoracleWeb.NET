# PoracleNG Version Compatibility

PoracleWeb.NET works against any PoracleNG from 5.1.0 upwards, and switches on the extra features of a
newer one when it finds them. There is nothing to configure.

The line that matters is **5.1.0 versus 5.2.1 and newer**. There was briefly a second question — whether
you were running PoracleNG's released `main` or its `develop` — and there no longer is: develop shipped
as 5.2.1 and merged. Anything written in terms of branches is out of date, including earlier drafts of
this page.

Version support still matters, and always will, because self-hosters upgrade on their own schedule. The
PGAN production instance moved to 5.2.1 on 2026-08-24 (migrations 5 through 8 applied cleanly); a server
somewhere is still on 5.1.0, and one is on something older than that.

!!! warning "5.1.0 is the minimum"
    Below it, per-alarm delivery scope, the PVP mega evolution filter and the minimum time-left filter
    write columns that do not exist, so those three controls save without complaint and change nothing.
    PoracleWeb.NET logs an error at startup and shows the version on **Admin → Settings**.

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
| Pokéstop-event tracking (`incident`) | v2 API surface | PoracleNG 5.2.1 |
| Mutes | v2 API surface | PoracleNG 5.2.1 |
| Pokecoin quest rewards (`reward_type: 8`) | Version | PoracleNG 5.2.1 |
| Rule descriptions on alarm cards | Response field | v1 `allProfiles`, or any v2 read |

Each of these carries its own small capability service, shaped like `ISummaryCapabilityService` and
resolving from `IPoracleServerProfileService`. There is deliberately no central registry: a registry
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
   answer for a user-facing control; `GET /api/summary-schedule/capability` is the pattern to copy.
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
PoracleWeb.NET is on v1.

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
