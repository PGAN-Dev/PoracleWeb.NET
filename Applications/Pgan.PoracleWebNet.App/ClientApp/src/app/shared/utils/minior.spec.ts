import { MINIOR_POKEMON_ID, includesMinior } from './minior';

describe('includesMinior', () => {
  it('finds Minior in a selection', () => {
    expect(includesMinior([52, MINIOR_POKEMON_ID])).toBe(true);
    expect(includesMinior(new Set([MINIOR_POKEMON_ID]))).toBe(true);
  });

  it('is false without it, including for All Pokemon (0)', () => {
    expect(includesMinior([])).toBe(false);
    expect(includesMinior([0, 52, 773, 775])).toBe(false);
  });
});
