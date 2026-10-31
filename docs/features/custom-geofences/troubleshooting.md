# Troubleshooting

Common problems operators hit, what causes them, and how to fix them. Each entry is symptom → cause → fix.

## The region dropdown is empty (or only shows "All")

**Symptom:** when a user draws a geofence, the region picker has nothing useful in it, and (in older builds) the Save button stays greyed out.

**Cause:** your Koji project is **flat** — no geofence is nested under another, so there are no regions to show. Regions are derived purely from parent/child nesting in Koji (see [Koji & regions](koji-and-regions.md#how-geofences-and-regions-relate-in-koji)).

**Fix — pick one:**

If you want regions, create parent geofences in Koji and nest your public areas under them; [Setting up regions](koji-and-regions.md#setting-up-regions-the-operator-how-to) has the steps. If you don't, there's nothing to do. The region is optional: the picker hides itself when there are no regions, and users can save a geofence without one.

## A user's private geofence isn't sending notifications

Work down this list:

1. **Is it switched on for the right profile?** A geofence is on/off **per profile**. If the user switched profiles, it may be off on the new one. Have them check the toggle on the Geofences page for the active profile.
2. **Is PoracleNG pointed at PoracleWeb.NET's feed?** The bot's geofence-source setting must be the combined feed URL (`http://poracleweb:8082/api/geofence-feed`), **not** Koji. If it points at Koji, private geofences will be missing entirely.
3. **Did PoracleNG reload its geofences?** PoracleWeb.NET tells PoracleNG to reload after changes, but if the bot was down at that moment, trigger a reload (or it'll pick it up on its next refresh).
4. **Is the alarm pointed somewhere else?** An alarm with its own delivery scope ignores the profile area list. If its scope chip reads "Within N km of ..." or "Only in ..." and the geofence isn't in that list, the geofence has nothing to do with whether that alarm fires. Open the chip on the alarm card to check or change it.
5. **Polygon too small or odd?** A shape needs at least 3 points and must be a real area. Degenerate shapes are dropped from the feed.

## A geofence saved but not switched on

**Symptom:** a user draws a geofence and sees "Saved, but it couldn't be switched on yet. Use the toggle to retry." The geofence is on the map, with the toggle off.

**Cause:** the geofence was saved, but PoracleNG didn't accept its name on the user's profile. Usually PoracleNG was unreachable or slow to reload its geofences at that moment. The order of the steps is what makes this possible:

```mermaid
sequenceDiagram
    participant SPA
    participant API as PoracleWeb.NET
    participant NG as PoracleNG
    SPA->>API: POST /api/geofences/custom
    API->>API: save the user_geofences row
    API->>NG: GET /api/geofence/reload
    NG->>API: GET /api/geofence-feed (now with the new fence)
    API->>NG: GET .../areas, then POST .../areas with trusted
    alt name accepted
        API-->>SPA: 201, active on the current profile
    else reload failed or name rejected
        API-->>SPA: 201 with activationFailed
    end
```

The `...` is `/api/v2/humans/{id}`. PoracleNG's trusted area write refuses a name it hasn't loaded, so the reload has to come first; posting the name first left every new fence inactive (#937). A reload can answer ok while PoracleNG keeps its old list because it couldn't reach the feed, which is why a rejected name is reported rather than assumed away. With `disable_areas` on, the fence is created inactive and nothing is attempted. A user already at PoracleNG's `[tracking] max_areas` gets the same message, because the new fence would be one area too many; their toggle then answers "You can select at most N areas. Remove one before adding another." rather than a conflict.

**Fix:** if the user is at the area limit, have them drop an area or switch another geofence off first. Otherwise have the user flip the toggle on the Geofences page. It retries once on its own. If it still fails, it answers a conflict error; check that PoracleNG is up and that it reads the combined feed (see the list above), then try again.

## Public (approved) geofences aren't showing up

1. **Wait up to 5 minutes, or force a refresh.** PoracleWeb.NET caches the Koji public list for 5 minutes. An approval clears that cache immediately, but a change made **directly in the Koji UI** won't be picked up until the cache expires — or until you call [`POST /api/geofence-feed/refresh`](koji-and-regions.md#forcing-a-refresh-after-a-change-in-koji).
2. **Check the Koji connection.** Wrong `KOJI_API_ADDRESS`, a bad `KOJI_BEARER_TOKEN`, or the wrong `KOJI_PROJECT_NAME` means PoracleWeb.NET can't read the public list. Remember the token is read **at startup**. Restart after changing it.
3. **Is it actually in the project?** A geofence must belong to your `KOJI_PROJECT_ID` to appear. Parent/region geofences are intentionally excluded (they're folders, not selectable areas).

## Koji is down — what happens?

The feed degrades in both directions rather than failing:

```mermaid
flowchart TD
    K[Koji unreachable] --> F[Feed serves the private user geofences<br/>from PoracleWeb.NET's own DB]
    D[PoracleWeb.NET DB unreachable] --> G[Feed serves the Koji public areas]
    B[BOTH unreachable] --> E[Feed answers 503, not an empty list]
```

For the first 5 minutes the cached Koji list covers the outage, so the feed is complete. After that the public areas drop out of the feed until Koji answers again; the private ones keep being served throughout.

**Why an error and not an empty list when both fail:** PoracleNG treats this feed as authoritative and keeps the last list it was given. An empty `200` is not a degraded answer — it reads as an instruction to forget every area every user has. A `503` leaves the bot's cached list in place.

!!! warning "You can't approve while Koji is down"
    Approving a submission writes to Koji, so approvals will error until Koji is reachable again. Everything else keeps working.

## Approval fails with a Koji error

The usual causes are an unreachable Koji or an auth problem (token/project). One specific gotcha worth knowing:

!!! info "The `__parent: 0` gotcha"
    Koji treats a geofence's parent as a real geofence ID. Sending a parent of `0` makes Koji reject the save with `[GEOFENCE]: Does not exist`. PoracleWeb.NET handles this for you — a geofence with **no** region is sent with a true "no parent" value, not `0` — so region-less approvals work. If you see this exact error from a custom integration, that's the cause.

## A geofence name shows in the bot when it shouldn't (or vice-versa)

Two flags control visibility, and PoracleWeb.NET sets them for you:

| | Private user geofence | Public (approved) geofence |
|---|---|---|
| Appears in the bot's `!area` picker | No | Yes |
| Name shown in notification DMs | No | Yes |

If a **private** geofence's name is leaking into the bot picker or DMs, something is serving it as public — check that PoracleNG reads PoracleWeb.NET's feed (not Koji directly), and that the geofence wasn't accidentally promoted.

## Capitalization / name-match issues

PoracleNG matches area names **case-sensitively**. PoracleWeb.NET always stores names in **lowercase**, so this normally just works. If you've hand-edited `humans.area`, `profiles.area`, or a geofence name in the database, make sure everything is lowercase — a single capital letter means a silent mismatch and no notifications.

## Removing a geofence from Koji completely

Deleting an approved geofence in PoracleWeb.NET removes it from the **project** (so it's no longer selectable), but the geofence row can still exist in Koji's database. To delete it **completely**, do it in the **Koji UI**. (PoracleWeb.NET intentionally does not hard-delete Koji geofences via the API.) This also applies to any stray test geofences — clean them up in the Koji UI.

## How the combined feed works (background)

So you understand why the bot only needs one URL: PoracleWeb.NET exposes **`/api/geofence-feed`**, which merges two sources into one list for PoracleNG. The diagram is on the [overview page](index.md#how-poracleng-gets-its-geofences-and-why-not-straight-from-koji).

- Public entries come from Koji, marked visible and selectable.
- Private entries come from PoracleWeb.NET's database, marked hidden and non-selectable, so the bot leaves them out of pickers and DMs.
- Region/parent geofences are filtered out (they're folders, not areas).
- Malformed polygons are skipped rather than served, so one bad shape can't break the feed for everyone.
- Either half can fail on its own and the other is still served; only a double failure returns an error (see [Koji is down](#koji-is-down-what-happens)).

!!! warning "Keep the feed on a private network"
    Reading the feed is open (no login) so PoracleNG can read it on your internal network. Don't expose it to the internet. The companion `POST /api/geofence-feed/refresh` is **not** open — it needs your `PORACLE_API_SECRET` in an `X-Poracle-Secret` header. See [Forcing a refresh](koji-and-regions.md#forcing-a-refresh-after-a-change-in-koji).
