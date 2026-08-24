/**
 * Optional PoracleNG features, offered only when the server behind this install can store them.
 *
 * The twin of `Core.Models/PoracleCapabilityKeys.cs`, in the same way `clean-flags.ts` twins
 * `CleanFlags.cs`. PoracleNG keeps a released `main` and a longer-running `develop` and PoracleWeb
 * supports both, so anything that exists on only one of them is asked about rather than assumed.
 *
 * The strings must match the backend exactly: they travel over `GET /api/settings/poracle-capabilities`
 * and nothing translates them on the way.
 *
 * These gate what is *rendered*. They are not the enforcement point — the alarm services refuse an
 * unsupported write independently and answer 409, because client state is trivially tampered with and
 * quick-pick apply and profile import never pass through a dialog at all.
 */
export const PORACLE_CAPABILITIES = {
  /** Costume filtering on pokemon alarms. Needs `monsters.costume`, PoracleNG schema 6. */
  MONSTER_COSTUME: 'monster_costume',

  /** Pokecoin quest rewards (`reward_type: 8`). Needs PoracleNG 5.2.0. */
  QUEST_POKECOINS: 'quest_pokecoins',

  /** Costume filtering on raid alarms. Needs `raid.costume`, PoracleNG schema 7 — a separate migration. */
  RAID_COSTUME: 'raid_costume',
} as const;

export type PoracleCapability = (typeof PORACLE_CAPABILITIES)[keyof typeof PORACLE_CAPABILITIES];
