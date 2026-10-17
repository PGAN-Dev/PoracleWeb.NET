import { BACKGROUND, compose, hasUnknownBits, isBackground, isShiny, KNOWN, preserve, SHINY } from './personal-flags';

describe('personal-flags', () => {
  it('constants match the PoracleNG bitmask', () => {
    expect(SHINY).toBe(1);
    expect(BACKGROUND).toBe(2);
    expect(KNOWN).toBe(3);
  });

  it.each([
    [0, false],
    [1, true],
    [2, false],
    [3, true],
    [4, false],
    [5, true],
  ])('isShiny(%i) === %s', (personal, expected) => {
    expect(isShiny(personal)).toBe(expected);
  });

  it.each([
    [0, false],
    [1, false],
    [2, true],
    [3, true],
    [4, false],
    [6, true],
  ])('isBackground(%i) === %s', (personal, expected) => {
    expect(isBackground(personal)).toBe(expected);
  });

  it.each([
    [0, false],
    [3, false],
    [4, true],
    [5, true],
    [8, true],
    [15, true],
  ])('hasUnknownBits(%i) === %s', (personal, expected) => {
    expect(hasUnknownBits(personal)).toBe(expected);
  });

  it.each([
    [false, false, 0],
    [true, false, 1],
    [false, true, 2],
    [true, true, 3],
  ])('compose(%s, %s) === %i', (shiny, background, expected) => {
    expect(compose(shiny, background)).toBe(expected);
  });

  describe('preserve', () => {
    it('keeps a reserved bit when both known bits are cleared', () => {
      expect(preserve(5, KNOWN, 0)).toBe(4);
    });

    it('keeps a reserved bit when both known bits are set', () => {
      expect(preserve(4, KNOWN, 3)).toBe(7);
    });

    it('ignores changes outside the mask', () => {
      expect(preserve(1, KNOWN, 1 | 4)).toBe(1);
    });
  });
});
