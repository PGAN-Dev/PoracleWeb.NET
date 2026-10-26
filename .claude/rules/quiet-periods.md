---
paths:
  - "**/*Mute*"
  - "**/*mute*"
  - "**/*quiet*"
  - "**/*Summary*"
---

## Quiet Periods (PoracleNG Mutes)

A quiet period is a time-boxed `(scope, value)` mute on a human, over PoracleNG's `/api/v2/humans/{id}/mutes`.
`IPoracleMuteProxy` is the **only /api/v2 caller in the codebase**: v2 is huma-generated, so its error
envelope is `{title, status, detail}` plus an `errors[]` array for schema violations. A handler that reads
`err.error.error` -- v1's shape, and what every other proxy here does -- renders blank against it.

It is **not** *Pause Alerts*, which toggles the human's `enabled` flag: account-wide, indefinite, persisted.

Four traps, all verified by calling a live instance (5.2.1, then 5.4.0) rather than reading the source:

**The store is in memory and dies with the processor.** Nothing about a mute is persisted. The UI therefore
shows a relative countdown and never an absolute expiry -- "Quiet until 3:15pm" is a promise the server
cannot keep, and a deadline that vanishes early reads as a lie where a countdown reads as time passing.
`MuteService` refetches on window focus and every time a chip appears, and the sheet states the volatility
in one plain line. Do not cache the list across a page's lifetime.

**POST canonicalises the area name; DELETE matches the stored string exactly.** `scope=area value=aberdeen`
is stored and returned as `Aberdeen` (the fence's own casing), and `DELETE ?value=aberdeen` then 404s.
PoracleWeb.NET holds area names lowercase everywhere because Poracle's matching is case-sensitive, so the
resume path must send back the value the LIST returned, and the chip must match the area row
case-insensitively. Get this wrong and the user has a quiet period nobody can lift until it expires.

**`scope=tracking` is deliberately not offered.** Tracking uids are per-table auto-increments whose ranges
overlap heavily on live data -- gym 31-121 sits wholly inside raid 59-343 and monster 4-36480 -- and
upstream's matcher compares the uid with no type qualifier. "Quiet this one rule" would silently quiet up
to ten unrelated rules and light the chip on their cards too, since the mute list is scope+value only and
cannot say which type it meant. That needed an upstream fix, not a client workaround, and
jfberry/PoracleNG#328 §6 (5.4.0) is it: GET returns `tracking_type` on a tracking mute and DELETE requires
`?tracking_type=` for that scope. We still create none, but we list and lift the bot's, so
`Mute.TrackingType` is read from the list and sent back on DELETE (SPA `trackingType` → API → upstream
`tracking_type`) **only when the list returned one** -- the same send-back-what-the-LIST-said rule as area
casing. The gate is still the right shape, not a leftover: a mute on any other scope has no type, and
the mutes DELETE rejects unknown query parameters (`422 unknown query parameter`, verified 2026-10-20). The
SPA keys a mute by scope, type and value, since raid 60 and pokemon 60 are two rows. Creating `scope=tracking`
mutes is possible now; that is a separate decision. `scope=pokestop` is
read-only here for a duller reason: neither the lure nor the invasion model carries a fort id.

**There is no capability check**, and `MuteCapabilityService` is deleted. The mute routes arrived in 5.2.0, so every supported server (5.4.0 and later) has them. `GET /api/mutes` answers `{ mutes }`, and all three actions answer `503` with a message when PoracleNG cannot be reached (`PoracleUnreachableExceptionFilter`, also on the Location controller and all eleven alarm controllers), so the SPA toasts the failure once instead of drawing a chip that claims nothing is quiet. Do not add a `Supports("mutes")` check against `/health`: 5.2.1's map has no mutes key, and absent means false by that map's own contract.

Two smaller ones. **Nobody validates a gym or station id**: `scope=gym value=abc123` is accepted verbatim,
so only ever create those from an existing alarm's `gymId`/`stationId`, never from free text. And **mutes
are per-human, not per-profile** -- switching profile does not lift them, and the copy must not imply it does.

Writes are refused while impersonating (`IsImpersonating` on `MuteController`), because `UserId` is the
inspected account and an unguarded write silences somebody else's alerts -- the #663 shape. Reads stay open
so an admin can diagnose "why am I getting nothing".

`quiet-surface-parity.spec.ts` pins the six surfaces that carry the chip and the four list components that
must not, so a future alarm type cannot quietly miss it.
