import { MIN_TIME_PRESETS_SECONDS, minTimeLabel, minTimeOptions, minTimePillLabel } from './min-time';

describe('minTimeOptions', () => {
  it('offers the presets when the rule has no filter', () => {
    expect(minTimeOptions(0)).toEqual([...MIN_TIME_PRESETS_SECONDS]);
  });

  it('offers the presets unchanged when the rule already holds one of them', () => {
    expect(minTimeOptions(300)).toEqual([...MIN_TIME_PRESETS_SECONDS]);
  });

  it('keeps a value the bot set that is not a preset, in order', () => {
    // Without this the select renders blank and the next save silently drops the filter.
    expect(minTimeOptions(137)).toEqual([0, 60, 120, 137, 300, 600, 900, 1200]);
  });
});

describe('minTimeLabel', () => {
  it('calls no filter what it is', () => {
    expect(minTimeLabel(0)).toEqual({ key: 'POKEMON.MIN_TIME_ANY' });
  });

  it('reads whole minutes as minutes', () => {
    expect(minTimeLabel(300)).toEqual({ key: 'POKEMON.MIN_TIME_MINUTES', params: { count: 5 } });
  });

  it('falls back to seconds for a value that is not whole minutes', () => {
    expect(minTimeLabel(137)).toEqual({ key: 'POKEMON.MIN_TIME_SECONDS', params: { count: 137 } });
  });
});

describe('minTimePillLabel', () => {
  it('says the pill is a floor, not a duration', () => {
    expect(minTimePillLabel(300)).toEqual({ key: 'POKEMON.PILL_TIME_LEFT_MINUTES', params: { count: 5 } });
  });

  it('falls back to seconds', () => {
    expect(minTimePillLabel(137)).toEqual({ key: 'POKEMON.PILL_TIME_LEFT_SECONDS', params: { count: 137 } });
  });
});
