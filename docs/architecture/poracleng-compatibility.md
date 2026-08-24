# PoracleNG Version Compatibility

PoracleWeb.NET works against any PoracleNG from 5.1.0 upwards, and switches on the extra features of
a newer one when it finds them. There is nothing to configure and no branch to declare.

That matters in two situations, and they are the same problem wearing different clothes:

| Situation | Example |
|---|---|
| Running PoracleNG's `develop` rather than the released `main` | 5.2.x while the release is 5.1.0 |
| Running a release, but not the newest one | 5.1.0 after 5.2.x has shipped |

The second is the common one and it never goes away, because self-hosters upgrade on their own
schedule. Everything below is written in terms of **versions**, not branches, for that reason.

!!! warning "5.1.0 is the minimum"
    Below it, per-alarm delivery scope, the PVP mega evolution filter and the minimum time-left
    filter write columns that do not exist. PoracleWeb.NET logs an error at startup and shows the
    version on **Admin → Settings**.

## How support is decided

Three signals, probed together and cached for five minutes. **Admin → Settings → Refresh** re-reads
them, which is what to press after upgrading PoracleNG.

| Signal | Source | Answers |
|---|---|---|
| Version | `GET /health` | Which release line. Used only where nothing better exists. |
| Capability map | `GET /health` → `capabilities` | Whether a bot or template-editor feature is compiled in. PoracleNG's own contract is that a missing key means unsupported. |
| Schema version | PoracleNG's `schema_migrations` table | Whether an alarm column exists. This is the honest signal for filter fields, because the capability map says nothing about them. |

Each optional feature names the narrowest signal that actually predicts it, rather than branching on
the version everywhere. Version answers "which release line", not "can this server store this field",
and the two come apart constantly: self-hosters cherry-pick, forks carry one feature and not another,
and a locally built binary reports `0.0.0`.

**Every gate fails closed.** An unreachable server, an unread migration number or an unparseable
version all resolve to unsupported. Hiding a control that would have worked is a nuisance; showing
one that silently writes a column the server does not have is the failure this exists to prevent.

The resolved list is served to the browser by `GET /api/settings/poracle-capabilities`. That decides
what is *rendered* — it is not the enforcement point. The alarm services refuse an unsupported write
independently and answer **409 Conflict** naming what the server would need, so quick-pick apply and
profile import are covered too.

## Optional features

| Feature | Needs | Behaviour below that |
|---|---|---|
| Pokecoin quest rewards (`reward_type: 8`) | PoracleNG 5.2.0 | The **PokéCoins** tab is absent from the quest dialog. Existing pokecoin rules stay visible and deletable. |
| Costume filter on pokemon alarms | PoracleNG schema 6 | Not yet exposed — see below. |
| Costume filter on raid alarms | PoracleNG schema 7 | Not yet exposed — see below. |

Costume filtering is gated and ready but has no UI, because PoracleNG serves no costume-name list:
the data is loaded into its game data and translated under `costume_{id}` keys, but
`/api/masterdata/` offers only `monsters` and `grunts`. A numeric costume id box would be worse than
nothing. Filed upstream; the control ships when there is something to populate it with.

Costume values set elsewhere are already safe at every version. `TrackingFieldPreserver` and
`PoracleJsonHelper.RewriteRows` carry forward every stored field PoracleWeb.NET has no model for, so
a costume set with the bot survives a web edit.

## Things that are the same at every supported version

Worth stating, because they are the ones most likely to look like they need a version check:

- **The v1 API is frozen and supported.** PoracleNG describes it as "deprecated-but-supported" with
  no sunset date. PoracleWeb.NET stays on v1.
- **Human and profile response shapes are identical.** 5.2.x added OpenAPI `doc:` annotations and
  changed no JSON.
- **Master data is identical.** `/api/masterdata/monsters` and `/grunts` moved from Gin to huma in
  5.2.x and kept the same body.
- **Error shapes changed, harmlessly.** The read endpoints in 5.2.x return RFC 9457
  `application/problem+json` instead of `{status, message}`. `PoracleApiProxy` reads status codes and
  never error bodies, and the tracking endpoints that *do* have their error text parsed are still Gin.
- **`disabledHooks` differs and needs no check.** 5.1.0 reports a vestigial `pokestop`, which maps to
  nothing; 5.2.x replaced it with `fort`, which is mapped. Each name is simply absent from the other
  version, so one map serves both.
- **Dedup and collision behaviour is identical.** `processor/internal/db/diff.go` is byte-for-byte the
  same across these versions, so `TrackingUpdateReconciler` stays correct.

One genuine difference with no UI consequence: migration 8 drops the legacy `UNIQUE` keys on the
tracking tables. A create that PoracleNG classifies as an insert but that collides on
`invasion_tracking` or `lure_tracking` fails at the database below schema 8 and succeeds at or above
it. It surfaces as a refused create, not as corruption.

## Adding a capability

When a newer PoracleNG grows something worth surfacing:

1. Add the constant and a rule to `Core.Models/PoracleCapabilityKeys.cs`, choosing the narrowest
   signal that predicts it. Give it a `Requires` string in words — it is what the 409 and the startup
   log say.
2. Add a row to `PoracleCapabilityServiceTests.Matrix` stating what each version answers.
   `EveryCapabilityIsInTheMatrix` fails the build otherwise.
3. Guard the service write path with `IPoracleCapabilityService.EnsureSupportedAsync`, on every
   create/update/bulk path, so quick-pick apply and profile import are covered.
4. Gate the SPA control on `SettingsService.supportsPoracle`, using the key from
   `shared/utils/poracle-capabilities.ts`. Prefer absent over disabled-with-an-explanation: there is
   nothing the user can do about their operator's PoracleNG version.
5. Add a row to the table above.

Reading and deleting an existing rule is **never** gated, only creating one. A rule can already exist
— set with the bot, or left behind by a PoracleNG downgrade — and a row nobody can see is a row
nobody can delete.
