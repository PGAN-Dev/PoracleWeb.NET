/**
 * The three pokestop events PoracleNG can alert on.
 *
 * A mirror of `pokestopEvent` in PoracleNG's `resources/data/util.json`, and the twin of
 * `PokestopEventTypes.cs`. The backend partitions the shared invasion table on `name`; this side
 * labels and colours by `displayType`. `PokestopEventTypesTests.TheFrontendTwinAgreesEntryForEntry`
 * fails the build if the two drift.
 *
 * The labels are the strings the invasion pages already use, so all eleven locales already carry
 * them translated.
 */

const UICONS_BASE = 'https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons';

export interface PokestopEventInfo {
  /** Card and icon colour, from upstream's own palette. */
  color: string;
  /** Translation key for the event's name. */
  displayKey: string;
  /** The pokestop-event id. This is what the API calls `displayType`. */
  displayType: number;
  /** Material icon, used when there is no artwork. */
  icon: string;
  /** Artwork, where upstream has some. */
  imgUrl?: string;
  /** The lowercased name PoracleNG stores in `grunt_type`. */
  name: string;
}

export const POKESTOP_EVENTS: readonly PokestopEventInfo[] = [
  {
    name: 'showcase',
    color: '#03AEB6',
    displayKey: 'INVASIONS.EVENT_TYPES.SHOWCASE',
    displayType: 9,
    icon: 'emoji_events',
  },
  {
    name: 'kecleon',
    color: '#B3CA78',
    displayKey: 'INVASIONS.EVENT_TYPES.KECLEON',
    displayType: 8,
    icon: 'visibility_off',
    imgUrl: `${UICONS_BASE}/pokemon/352.png`,
  },
  {
    name: 'gold-stop',
    color: '#F9E418',
    displayKey: 'INVASIONS.EVENT_TYPES.GOLD_STOP',
    displayType: 7,
    icon: 'paid',
  },
];

/** The event with this id, or undefined for one this build has no entry for. */
export function pokestopEventInfo(displayType: number | null | undefined): PokestopEventInfo | undefined {
  return POKESTOP_EVENTS.find(e => e.displayType === displayType);
}
