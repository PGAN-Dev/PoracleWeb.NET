/**
 * The "minimum time left" filter, in the shape a person picks it.
 *
 * PoracleNG stores seconds and compares them against a spawn's remaining time, so the question the
 * filter answers is "can I still get there?". People answer that in minutes, and in round ones: every
 * rule in production that uses this field is set to exactly five. A closed list of presets rules out
 * both ways a free number field goes wrong here — typing 5 and meaning minutes, which silently asks for
 * five seconds, and typing something longer than a spawn lives, which mutes the rule with no error.
 */
export const MIN_TIME_PRESETS_SECONDS = [0, 60, 120, 300, 600, 900, 1200] as const;

/**
 * The presets, plus whatever the rule already holds.
 *
 * A rule set with the bot can carry any value at all, and a select that does not offer it would show
 * blank and quietly rewrite it on the next save.
 */
export function minTimeOptions(current: number): number[] {
  const options = [...MIN_TIME_PRESETS_SECONDS] as number[];
  return current > 0 && !options.includes(current) ? [...options, current].sort((a, b) => a - b) : options;
}

/** Translation key and params for one option. Whole minutes read as minutes; anything else as seconds. */
export function minTimeLabel(seconds: number): { key: string; params?: Record<string, number> } {
  if (seconds <= 0) return { key: 'POKEMON.MIN_TIME_ANY' };
  if (seconds % 60 === 0) return { key: 'POKEMON.MIN_TIME_MINUTES', params: { count: seconds / 60 } };
  return { key: 'POKEMON.MIN_TIME_SECONDS', params: { count: seconds } };
}

/**
 * Key and params for the card pill, which has to carry the comparison in the words: a bare "5 min" on a
 * card of thresholds reads as a duration rather than a floor.
 */
export function minTimePillLabel(seconds: number): { key: string; params: Record<string, number> } {
  return seconds % 60 === 0
    ? { key: 'POKEMON.PILL_TIME_LEFT_MINUTES', params: { count: seconds / 60 } }
    : { key: 'POKEMON.PILL_TIME_LEFT_SECONDS', params: { count: seconds } };
}
