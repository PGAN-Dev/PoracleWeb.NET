# Upgrading to PoracleNG 5.4.0

This release of PoracleWeb.NET needs PoracleNG 5.4.0 or newer. Upgrade PoracleNG first, then PoracleWeb.NET, then run the rule repair once.

PoracleWeb.NET still starts against an older PoracleNG, but it logs an error, the Versions card under **Admin > Settings** reports the mismatch, and calls to routes the older server lacks fail with a message naming the version they need.

## What users will notice

Most of the change is underneath. Every alarm read, add, edit, delete, bulk distance change and cleaning toggle now goes through PoracleNG's `/api/v2`, and nothing is written to PoracleNG's database any more. A few behaviours differ on screen:

- An Add that would take over another alarm is still refused with "Another alarm of this type already uses those settings". PoracleNG makes that call now, using its own dry run, where PoracleWeb.NET used to keep a copy of its rules.
- Adding a max battle for a boss you already track, at a second radius, is refused. PoracleNG 5.4.0 merges the two, so the Add would have quietly changed the first alarm's distance.
- Changing the distance on a selection updates every rule it can and reports how many it skipped because they would clash with another rule. It used to fail the whole selection.
- Invasion alarms saved as `metal` become `steel` the next time they're edited. `metal` never matched anything, because PoracleNG's game data calls the grunt `steel`.

## Run the rule repair

After PoracleNG is on 5.4.0, open **Admin > Settings** and find the **Rule repair** card. It looks for stored rules that 5.4.0 reads differently from the way they were saved, and fixes the ones whose meaning is clear.

Press **Scan** first. The scan reads every user's rules and writes nothing; you can run it as often as you like. It reports these kinds:

| Kind | What it is | What Repair does |
|---|---|---|
| Grunt type `metal` | An invasion alarm for a grunt PoracleNG now calls `steel` | Saves it as `steel` |
| Raid with Pokémon 0 | A raid alarm whose boss is stored as 0 | Saves it as "by level" |
| Raid for any boss | A raid alarm for any boss, stored with the boss and level left undefined | Saves it as any boss, any tier |
| Max battle for any boss | The same shape on a max battle alarm | Saves it as any boss, any tier |
| Cannot be edited | A rule PoracleNG 5.4.0 won't save as it stands | Nothing. The card lists it so you can fix or delete it by hand |

Nothing changes until you press **Repair**. Each rule is then checked again just before it's written, so a rule that someone edited after the scan is judged on its current state. If PoracleNG says a write would be identical to the rule already stored, the card counts it as already fine. If it would duplicate another rule, the card leaves it alone and lists it as a conflict; one of the two rules is probably redundant. Each rule is rewritten from the values PoracleNG holds for it, with the one stored value changed, and it normally keeps its id.

If some users couldn't be read, the card lists them with the reason and opens that list, and it won't tell you nothing needs repairing. If no user could be read at all, the scan fails and says why.

Only one scan or repair runs at a time, and a second start is refused while one is running. A run stops if PoracleNG stops answering (or a proxy in front of it keeps answering 502, 503 or 504) or turns out to be too old; repairs already written stay written, and you can scan again afterwards. The job's state lives in memory, so if PoracleWeb.NET restarts during a scan or repair, scan again; running it again is safe. Only admins can use the card, and not while impersonating a user. The server log records which admin started each run, and, just before each repair is written, the rule as it was stored, so a repair can be undone by hand.

You can run the repair again at any time. A rule that has been repaired no longer shows up in the next scan.

## Rules on a profile with no profile row

PoracleNG's v2 read of all of a user's profiles skips rules whose `profile_no` has no matching row in the profiles table ([jfberry/PoracleNG#330](https://github.com/jfberry/PoracleNG/issues/330)). Such rules never fire, because PoracleNG matches alerts against profiles that exist. They're also invisible to PoracleWeb.NET and to the scan above, so they won't appear in any list and the repair can't reach them. This comes from PoracleNG and has to be fixed there; until it is, there's nothing to do from the web interface.

## The database settings are still required

Keep the `DB_*` settings in your `.env`. At startup PoracleWeb.NET still reads the old `pweb_settings` table in PoracleNG's database to carry any remaining settings, webhook delegates and quick picks into its own database, and it widens that table's `value` column first. Nothing else reads or writes PoracleNG's tables. A later release will drop this and the settings with it.

## Order of steps

1. Back up both databases.
2. Upgrade PoracleNG to 5.4.0 and confirm it's running.
3. Update PoracleWeb.NET (`./scripts/docker.sh update` for Docker installs).
4. Check the Versions card under **Admin > Settings** shows no mismatch.
5. Scan, review, then Repair on the Rule repair card.

See [PoracleNG Version Compatibility](../architecture/poracleng-compatibility.md) for what changed in the integration and why.
