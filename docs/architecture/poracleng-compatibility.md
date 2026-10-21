# PoracleNG Version Compatibility

PoracleWeb.NET requires **PoracleNG 5.4.0 or newer**. There is nothing to configure, and no feature
switches on or off with the server's version.

!!! warning "5.4.0 is the minimum"
    PoracleWeb.NET still starts against an older PoracleNG, because refusing to start would take the
    whole site down over something an operator can fix in minutes. It logs an error at startup and the
    Versions card on **Admin → Settings** says the server is too old. Beyond that it makes no allowance:
    it calls PoracleNG's `/api/v2` routes for humans, profiles, areas, places and mutes with no v1
    fallback, and a route that doesn't exist answers with a message naming the version it needs.

    Upgrade PoracleNG before taking a PoracleWeb.NET release that requires a newer one.

## Why 5.4.0

PoracleNG 5.4.0 is the release that carries everything PoracleWeb.NET asked upstream for while working
out how to stop reading and writing PoracleNG's database (jfberry/PoracleNG#328; the requests are listed
under [Upstream history](../poracleng-enhancement-requests.md)). Supporting an older server meant keeping
each workaround beside the code that replaced it, so the fallbacks were deleted instead of gated. What
went in the first step, which covers humans, profiles, places, mutes and geocoding:

| Removed | What it did on an older PoracleNG |
|---|---|
| `HumanRepository`, `ProfileRepository` | Listed and deleted humans, renamed profiles and wrote a new profile's areas and location straight into PoracleNG's tables. Admin list and delete now use `GET /api/v2/humans` and `DELETE /api/v2/humans/{id}`; rename and new-profile geography use the v2 profile, area and location routes |
| `ProfileNumbering` (list, create, re-list, diff) | Worked out which number PoracleNG gave a new profile, because the create answered `{"status":"ok"}` and nothing else. The create now returns it |
| The v1 branch of every single-human call | `/api/humans/...` and `/api/profiles/...` fallbacks behind a cached "v2 is absent" flag |
| `MuteCapabilityService`, `PlaceUpdateCapabilityService`, `QuestPokecoinCapabilityService`, `CostumeCapabilityService` | Asked whether the server could do the thing before the SPA showed the control. Quiet periods, moving a saved place, Pokécoin quest rewards and costume filters are always offered |
| The legacy geocoder call | Called the geocoder PoracleNG was configured with, as Nominatim, when PoracleNG had no `/api/geocode` routes |
| `ProbePokestopEventsAsync` | Probed for the Pokéstop Events page on a server without it |
| `Poracle:TrackingApiVersion` (`PORACLE_TRACKING_API_VERSION`) | Pinned the tracking surface for a fork whose version string lied. A leftover setting is ignored |
| Eleven `PoracleV2Capabilities` flags | Each asked the server's `/openapi.json` whether one field or route existed. `TrustedSetAreas` and `InvasionGruntType` remain until areas and tracking move to v2 |

The v1 tracking API is **not** gone from this application. Tracking reads, creates and the bulk
distance and clean writes still use it, and so does a plaintext-404 fallback on the single-rule edit
routes. User-drawn geofence names still reach PoracleNG's area columns through a direct database write.
Both are later steps, and this page changes again when they land.

## How support is decided

Two probes, cached for five minutes. The mechanics are in
[Backend Patterns](backend.md#server-capability-probe).

| Signal | Source | Answers |
|---|---|---|
| Version | `GET /health` | Whether this server is known to be older than the minimum. |
| Published schema | `GET /openapi.json` | The two questions still open: whether `setAreas` takes `trusted`, and whether the invasion rule declares `grunt_type`. |

`PoracleServerProfile.IsSupported` is true when PoracleNG answered and its version is not below
`MinimumSupported`. An unparseable version, or the `0.0.0` a locally built binary reports because its
build flags were never injected, counts as current: a build of today's source is newer than 5.4.0, and
hiding features from it on a guess helps nobody. An unreachable server is not supported.

`GET /health` also carries a capability map, which covers bot and template-editor features and nothing
about alarms. A missing key there means unsupported, PoracleNG's own contract for the map, so reaching for
`Supports()` to answer an alarm question quietly answers false forever.

## What still depends on the server

| Feature | Signal | Needs |
|---|---|---|
| Alarm edits and deletes through `/api/v2` | Version, then a plaintext 404 | A supported PoracleNG. A route that answers gin's plaintext `404 page not found` is remembered as absent for five minutes and the write goes to v1. Deletes fall back to v1 for a rule outside the active profile, which v2 refuses and v1 deletes |
| Pokéstop-event tracking (`incident`) | Poracle's own `general.disable_showcase` | Only `true` hides the page. Absent or unreadable leaves it on |
| Invasion edits through `/api/v2` | Published schema, then game data | A `V2InvasionRule` declaring `grunt_type`, **and** that server's grunt masterdata listing the rule's own name |
| Pokemon edit-in-place | Poracle's `tracking.pokemon_edit` config | The key `true`. Absent or unreadable hides the toggle |
| Translated grunt display names | Response field | A `/api/masterdata/grunts` entry carrying `grunt_type` and `short_name` |
| Rule descriptions on alarm cards | Response field | v1 `allProfiles`, or any v2 read |

The two rows answered from the published schema come from `IPoracleV2SchemaService`, which reads the
target instance's own `/openapi.json`. It is the schema-document sibling of `PoracleServerProfile`: one
cached probe, typed questions, everything false when the document cannot be read.

Invasion needs a second question after the first, because a server carrying the field still refuses a
`grunt_type` its grunt masterdata does not list. `InvasionGruntNameService` asks it, and fails closed to
an empty set, which routes every invasion edit to v1.

`SummaryCapabilityService` and `PokemonEditCapabilityService` answer from Poracle's configuration, not
from the version, because quest summary delivery and pokemon edit-in-place are deployment settings an
operator can switch off on a server that supports them. Pokéstop Events are decided by
`UpstreamFeatureFlagService` for the same reason, with `disable_showcase`.

There is deliberately no central registry. A registry was written and abandoned, because the per-feature
shape already existed and two mechanisms answering one question is how one of them ends up being the one
nobody updates.

Costume names are the awkward one. PoracleNG loads them into its game data under `costume_{id}` keys
but publishes them nowhere (`/api/masterdata/` offers only `monsters` and `grunts`), so the costume
picker reads the same WatWowMap masterfile PoracleNG itself reads. The names are therefore English in
every language, and a costume too new for that file shows as its number.

`TrackingFieldPreserver` and `PoracleJsonHelper.RewriteRows` carry forward every stored field
PoracleWeb.NET has no model for, so a field set with the bot survives a web edit whatever the server is.

Rule descriptions belong on this list too. A v2 tracking read takes `?include_descriptions=true` and
returns a `description` on each rule, the same sentence PoracleNG's bot answers a `!pokemon` command
with. It is easy to conclude otherwise: `description` is not on the named `V2PokemonRule` request
schema, and the response envelope that carries it is an inline object under `rules.items` rather than a
named component, so a search of `components/schemas` finds nothing. Resolve
`V2ListOutput...Body.properties.rules.items` before believing a field is absent.

Reading and deleting an existing rule is **never** gated, only creating one. A rule can already exist
(set with the bot, or left behind by a downgrade), and a row nobody can see is a row nobody can delete.

## When a request cannot be served

On a server below 5.4.0 a human, profile, area, place or geocode route answers gin's plaintext
`404 page not found`. Telling that apart from a real "not found" is the whole job:
`PoracleProblemDetails.IsProblemJson` says whether the body is PoracleNG's own answer. A problem+json 404
keeps its meaning (no such human, no such profile, nothing at this coordinate). A plaintext one throws
`PoracleUnsupportedException(feature, "PoracleNG 5.4.0")`, and the global
`PoracleUnsupportedExceptionFilter` turns it into **409 Conflict**:

```json
{
  "error": "This PoracleNG does not support listing areas. It requires PoracleNG 5.4.0.",
  "feature": "listing areas",
  "requires": "PoracleNG 5.4.0"
}
```

409 rather than 403 on purpose. The 403 branch belongs to `disable_*` site settings and keys off a
`disableKey` in the body; borrowing it would put an operator's switch and a version shortfall behind the
same toast, and the version shortfall has the one detail worth reading in it. 409 has no interceptor
branch, so it falls through to the caller, which shows the message beside the control that caused it
(the same route `TrackingConflictExceptionFilter` takes). Address search is the exception: its caller
swallows the 409, so on a too-old server the search just returns nothing.

A separate filter, `PoracleUnreachableExceptionFilter`, answers **503** with a message when PoracleNG
cannot be reached at all. It is applied to the mute and location controllers and to all eleven alarm controllers, where a failed read
used to look like an empty answer: a quiet-period chip claiming nothing is quiet, or a places list that
said the user had none.

Throw `PoracleUnsupportedException` from the service layer, not from a controller. Quick-pick apply,
profile duplicate and profile import all reach the alarm services without passing an action that could
have checked first.

## Adding a feature that needs a newer PoracleNG

1. **Raise the minimum, or gate on what the server publishes.** A feature every supported server has
   needs no gate. One that only some do goes through `IPoracleV2SchemaService`: add a typed question,
   read it from the target's `/openapi.json`, answer false when the document cannot be read. Don't add a
   version compare; the minimum is the only version PoracleWeb.NET checks.
2. **Write a small per-feature service** if the SPA needs to ask, shaped like
   `ISummaryCapabilityService`: one method, one question, answering false when it cannot tell.
3. **Guard the service write path**, on create, update and bulk alike, and throw
   `PoracleUnsupportedException(feature, requires)` with words the user can act on.
4. **Give the SPA a way to ask.** `GET /api/admin/server-profile` is admin-only, so it cannot be the
   answer for a user-facing control. An ordinary authenticated endpoint answers instead, like
   `GET /api/summary-schedules/capability`.
5. **Prefer hiding the control to disabling it with an explanation.** There is nothing the user can do
   about their operator's PoracleNG version.
6. **Add a row to the table above.**

## Raising the minimum again

Move `PoracleServerProfile.MinimumSupported`, then delete what only the old floor needed, as 5.4.0 did.
Before deleting a fallback, ask whether it is there for the old version or for a server that answers
differently today. The plaintext-404 handling on each v2 route was the first kind for humans and
profiles, which is why it now throws, and is still the second kind for single-rule tracking edits until
tracking moves to v2 entirely.

## Things that look like they differ between versions and do not

These are the ones most likely to send you writing a check that buys nothing.

**The v1 API is frozen.** Its release notes describe RFC 9457 `application/problem+json` error bodies and
422 status codes, and those are real, on `/api/v2` only. A v1 tracking POST for a user that does not
exist answers `{"message":"User not found","status":"error"}`; a malformed v2 path parameter answers 422
with `Content-Type: application/problem+json` and an `errors` array. Assuming the new shapes applied
everywhere cost a wrongly framed issue and pull request. Each proxy reads errors through
`PoracleProblemDetails`, which handles both dialects. See
[PoracleNG API Proxy](poracleng-proxy.md#the-v2-write-path).

**Do not take `active_hours` day numbering from PoracleNG's OpenAPI schema.** On older builds
`V2ActiveHourEntry.day` was declared `minimum: 0, maximum: 6` and described as "0=Sunday … 6=Saturday".
The scheduler uses ISO weekdays, Monday 1 through Sunday 7 (`isoDow` in
`processor/cmd/processor/profiles.go`), and PoracleWeb.NET validates 1 to 7.
`ProfileController.ValidateActiveHours` is the place that would be wrong if somebody "fixed" it against a
stale document.

**The `/health` capability map is not where alarm features appear.** It covers bot and template-editor
features (`buttons`, `snapshots`, `autocreate`, `tomlDts`, `costume`, `customMaps` and the like) and says
nothing about incidents or mutes. Reaching for `Supports()` for an alarm feature will quietly answer false
forever.
