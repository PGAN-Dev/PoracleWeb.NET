# PoracleNG Version Compatibility

PoracleWeb.NET needs **PoracleNG 5.2.1 or newer**, and switches on the features of a newer build when
that build says it can serve them. There is nothing to configure.

!!! warning "5.2.1 is the minimum"
    PoracleWeb.NET still starts against an older PoracleNG, because refusing to start would take the
    whole site down over something an operator can fix in minutes. It logs an error at startup and the
    Versions card on **Admin → Settings** says the server is too old. What breaks on 5.1.0:

    - Editing a lure or invasion alert can answer a server error, and so can adding a second lure or
      invasion alert for a lure type or grunt the user already tracks (one limited to an area, say).
      5.1.0's database still carries the unique keys that refuse those rows.
    - Fort changes switched off with `general.disable_fort_update` stay on offer, because 5.1.0 leaves
      `fort` out of `disabledHooks`.
    - Pokéstop Events, quiet periods, Pokécoin quest rewards, costume filters and moving a saved place
      are hidden.

    Upgrade PoracleNG before taking a PoracleWeb.NET release that requires a newer one.

## Why 5.2.1

5.2.1 was raised to the minimum on 2026-10-20, once production had run 5.2.x for two months and then a
5.3.0 build. What it removed, all of which existed only to keep 5.1.0 working:

| Removed | What it did on 5.1.0 |
|---|---|
| `INaturalKeyCapabilityService`, `NaturalKeyTrackingUpdate` | Read whether `lures` and `invasion` still carried their unique keys, and if so refused a second rule on the natural key and edited by deleting the row and re-creating it |
| `PoracleSchemaVersionReader` | Read `schema_migrations` and `information_schema` straight out of PoracleNG's database, for the check above and for the costume columns |
| The `general.disable_fort_update` read | A second `GET /api/config/values` for a server too old to report `fort` in `disabledHooks`, recognised by the absence of `availableLanguages` |
| The per-feature `>= 5.2.0` compares | Mutes, Pokécoin rewards, moving a place and both v2 surfaces each compared the version against 5.2.0; they now share one rule, below |

5.2.1 rather than 5.2.0 because 5.2.1 is the first release that reports `fort` and `availableLanguages`
on its config, and the one production verified everything against. Migration 8, which drops the lure and
invasion keys, is in both.

The v1 API is **not** gone. PoracleNG still serves it on 5.2.1 and later, every read and create still
uses it, and the v2 paths fall back to it when a fork or a newer build answers differently. What went is
the code that existed because of 5.1.0, not the code that speaks v1.

## How support is decided

Two sources, probed together and cached for five minutes. The mechanics are in
[Backend Patterns](backend.md#server-capability-probe).

| Signal | Source | Answers |
|---|---|---|
| Version | `GET /health` | Whether this server is known to be older than the minimum. |
| Published schema | `GET /openapi.json` | Whether a route or field that arrived after 5.2.1 exists on this server. |

`GET /health` also carries a capability map, which covers bot and template-editor features and nothing
about alarms. A missing key there means unsupported, PoracleNG's own contract for the map, so reaching for
`Supports()` to answer an alarm question quietly answers false forever.

**The version decides one thing: whether a server is known to be too old.** `PoracleServerProfile.IsSupported`
is true when PoracleNG answered and its version is not below `MinimumSupported`. An unparseable version, or
the `0.0.0` a locally built binary reports because its build flags were never injected, counts as current:
a build of today's source is newer than 5.2.1, and hiding features from it on a guess helps nobody. An
unreachable server is not supported, so the features it would decide stay hidden until it answers.

Everything that arrived after 5.2.1 is decided by the published schema, never by a version number. The
branch carrying those features reports `5.3.0`, no tagged release carries most of them, which release they
land in is not knowable from here, and a fork that cherry-picks one fix reports whatever it likes.

## What depends on the server version

| Feature | Signal | Needs |
|---|---|---|
| Costume filters, quiet periods, Pokécoin quest rewards, moving a saved place | Version | A supported PoracleNG (`IsSupported`) |
| Alarm edits and deletes through `/api/v2` | Version | A supported PoracleNG, or `Poracle:TrackingApiVersion=v2`. Deletes fall back to v1 for a rule outside the active profile, which v2 refuses and v1 deletes |
| Pokéstop-event tracking (`incident`) | Poracle's own `general.disable_showcase` | The key set to `false`. Absent or unreadable keeps the page hidden |
| Invasion edits through `/api/v2` | Published schema, then game data | A `V2InvasionRule` declaring `grunt_type`, **and** that server's grunt masterdata listing the rule's own name |
| Profile create reporting its number | Published schema | A create response whose body declares `profile_no` |
| Profile rename through `PATCH` | Published schema | A `V2UpdateProfileBody` declaring `name` |
| A mention (`ping`) surviving an edit | Published schema | `V2PokemonRule` declaring `ping` (jfberry/PoracleNG#249) |
| Translated grunt display names | Response field | A `/api/masterdata/grunts` entry carrying `grunt_type` and `short_name` |
| Rule descriptions on alarm cards | Response field | v1 `allProfiles`, or any v2 read |

The rows gated on the published schema are answered by `IPoracleV2SchemaService`, which reads the target
instance's own `/openapi.json`. It is the schema-document sibling of `PoracleServerProfile`: one cached
probe, several typed questions, everything false when the document cannot be read.

Invasion needs a second question after the first, because a server carrying the field still refuses a
`grunt_type` its grunt masterdata does not list. `InvasionGruntNameService` asks it, and fails closed to an
empty set, which routes every invasion edit to v1.

`MuteCapabilityService`, `QuestPokecoinCapabilityService`, `PlaceUpdateCapabilityService` and
`CostumeCapabilityService` are kept even though each now answers from `IsSupported` alone. The SPA asks
them before offering a control, and they are where a future refinement belongs if one of these features
ever needs more than "a supported server that answered". `SummaryCapabilityService` is the same shape over
a different source, since quest summary delivery is a deployment setting rather than a server capability,
and Pokéstop Events are decided by `UpstreamFeatureFlagService` because `disable_showcase` is a Poracle
config flag an operator can also switch off on a server that supports it.

There is deliberately no central registry. A registry was written and abandoned, because the per-feature
shape already existed and two mechanisms answering one question is how one of them ends up being the one
nobody updates.

Costume names are the awkward one. PoracleNG loads them into its game data under `costume_{id}` keys
but publishes them nowhere -- `/api/masterdata/` offers only `monsters` and `grunts` -- so the costume
picker reads the same WatWowMap masterfile PoracleNG itself reads. The names are therefore English in
every language, and a costume too new for that file shows as its number.

`TrackingFieldPreserver` and `PoracleJsonHelper.RewriteRows` carry forward every stored field
PoracleWeb.NET has no model for, so a field set with the bot survives a web edit whatever the server is.

Rule descriptions belong on this list too. A v2 tracking read takes `?include_descriptions=true` and
returns a `description` on each rule -- the same sentence PoracleNG's bot answers a `!pokemon` command
with. It is easy to conclude otherwise: `description` is not on the named `V2PokemonRule` request
schema, and the response envelope that carries it is an inline object under `rules.items` rather than a
named component, so a search of `components/schemas` finds nothing. Resolve
`V2ListOutput...Body.properties.rules.items` before believing a field is absent.

The v1 `allProfiles` endpoint accepts the same `includeDescriptions` flag, so this one has a path on
both surfaces.

Reading and deleting an existing rule is **never** gated, only creating one. A rule can already exist —
set with the bot, or left behind by a downgrade — and a row nobody can see is a row nobody can delete.

## When a request cannot be served

`PoracleUnsupportedException` carries a plain feature name and what the server would need, in words. The
global `PoracleUnsupportedExceptionFilter` turns it into **409 Conflict**:

```json
{
  "error": "This PoracleNG does not support quiet periods. It requires PoracleNG 5.2.1.",
  "feature": "quiet periods",
  "requires": "PoracleNG 5.2.1"
}
```

409 rather than 403 on purpose. The 403 branch belongs to `disable_*` site settings and keys off a
`disableKey` in the body; borrowing it would put an operator's switch and a version shortfall behind the
same toast, and the version shortfall has the one detail worth reading in it. 409 has no interceptor
branch, so it falls through to the caller, which shows the message beside the control that caused it —
the same route `TrackingConflictExceptionFilter` already takes.

**Nothing throws it yet.** Every gate so far hides its control before a request can be made, which is
the preferred outcome. The paths that still have to refuse something do it their own way: a costume write
to a server that is not supported throws `AlarmValidationException` and answers 400 beside the field, and
moving a saved place on such a server answers 501 with a sentence naming the workaround. Use the 409 for a
service-layer guard that has no field to point at.

Throw it from the service layer, not from a controller. Quick-pick apply, profile duplicate and profile
import all reach the alarm services without passing an action that could have checked first.

## Adding a gated feature

1. **Gate on what the server publishes about itself.** Anything newer than 5.2.1 goes through
   `IPoracleV2SchemaService`: add a typed question, read it from the target's `/openapi.json`, answer false
   when the document cannot be read. Do not add a version compare; the minimum is the only version
   PoracleWeb.NET checks.
2. **Write a small per-feature service** if the SPA needs to ask, shaped like
   `ISummaryCapabilityService`: one method, one question, answering false when it cannot tell. The
   interesting part of each gate is the sentence explaining which signal was chosen and why.
3. **Guard the service write path**, on create, update and bulk alike, and throw
   `PoracleUnsupportedException(feature, requires)` with words the user can act on.
4. **Give the SPA a way to ask.** `GET /api/admin/server-profile` is admin-only, so it cannot be the
   answer for a user-facing control. Ordinary authenticated endpoints answer instead:
   `GET /api/summary-schedules/capability`, `GET /api/settings/costume-capability` and
   `GET /api/quests/capability` each answer on their own. `GET /api/mutes` and
   `GET /api/location/places` are the pattern to copy — both fold the capability into the list response
   rather than answering separately, because the caller needs the list anyway and two calls would have
   been two calls every time.
5. **Prefer hiding the control to disabling it with an explanation.** There is nothing the user can do
   about their operator's PoracleNG version.
6. **Add a row to the table above.**

## Raising the minimum again

Move `PoracleServerProfile.MinimumSupported`, then delete what only the old floor needed. The last raise
is the worked example: every branch that existed only for the old version went, and every fallback that
covers a newer build or a fork stayed. Before deleting a fallback, ask whether it is there for the old
version or for a server that answers differently today. The plaintext-404 fallback on each v2 route, the
`Poracle:TrackingApiVersion` pin and the v1 paths behind the schema-gated features all cover the second.

## Things that look like they differ between versions and do not

These are the ones most likely to send you writing a check that buys nothing.

**The v1 API is frozen, and unchanged on 5.2.1.** Its release notes describe RFC 9457
`application/problem+json` error bodies and 422 status codes, and those are real — on `/api/v2` only.
Verified by calling both surfaces on the same 5.2.1 server: a v1 tracking POST for a user that does not
exist answers `{"message":"User not found","status":"error"}`, byte-identical to 5.1.0, while a
malformed v2 path parameter answers 422 with `Content-Type: application/problem+json` and an `errors`
array. Assuming the new shapes applied everywhere cost a wrongly framed issue and pull request.

Because v1 did not move, every read, every create and both distance endpoints stay on it. What speaks v2
is single-rule edits and deletes for the ten v1 tracking types (invasion behind its own gate), mutes, and
Pokéstop events — and each reads errors through `PoracleProblemDetails`, which handles both dialects. See
[PoracleNG API Proxy](poracleng-proxy.md#the-v2-write-path).

**Do not take `active_hours` day numbering from PoracleNG's OpenAPI schema.** `V2ActiveHourEntry.day` is
declared `minimum: 0, maximum: 6` and described as "0=Sunday … 6=Saturday". The scheduler uses ISO
weekdays, Monday 1 through Sunday 7 (`isoDow` in `processor/cmd/processor/profiles.go`). The schema
therefore rejects Sunday and accepts a meaningless 0. PoracleWeb.NET validates 1 to 7, which is correct;
`ProfileController.ValidateActiveHours` is the place that would be wrong if somebody "fixed" it against
the document.

**The `/health` capability map is not where alarm features appear.** Between 5.1.0 and 5.2.1 it gained
exactly one key, `derivedDtsTypes`, and says nothing about costume, incidents or mutes. It covers bot and
template-editor features. Reaching for `Supports()` for an alarm feature will quietly answer false forever.
