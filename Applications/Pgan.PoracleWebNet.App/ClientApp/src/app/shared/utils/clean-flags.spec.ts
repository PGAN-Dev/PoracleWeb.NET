import { ALL, AUTO_DELETE, compose, EDIT, isAutoDelete, isEdit, isSummary, preserve, SUMMARY } from './clean-flags';

describe('clean-flags', () => {
  it('constants match the PoracleNG bitmask', () => {
    expect(AUTO_DELETE).toBe(1);
    expect(EDIT).toBe(2);
    expect(SUMMARY).toBe(4);
    expect(ALL).toBe(7);
  });

  describe('isAutoDelete', () => {
    it.each([
      [0, false],
      [1, true],
      [2, false],
      [3, true],
      [5, true],
      [7, true],
    ])('isAutoDelete(%i) === %s', (clean, expected) => {
      expect(isAutoDelete(clean)).toBe(expected);
    });
  });

  describe('isEdit', () => {
    it.each([
      [0, false],
      [1, false],
      [2, true],
      [3, true],
      [6, true],
      [7, true],
    ])('isEdit(%i) === %s', (clean, expected) => {
      expect(isEdit(clean)).toBe(expected);
    });
  });

  describe('isSummary', () => {
    it.each([
      [0, false],
      [1, false],
      [4, true],
      [5, true],
      [6, true],
      [7, true],
    ])('isSummary(%i) === %s', (clean, expected) => {
      expect(isSummary(clean)).toBe(expected);
    });
  });

  describe('compose', () => {
    it.each([
      [false, false, false, 0],
      [true, false, false, 1],
      [false, true, false, 2],
      [true, true, false, 3],
      [false, false, true, 4],
      [true, false, true, 5],
      [true, true, true, 7],
    ])('compose(%s, %s, %s) === %i', (autoDelete, edit, summary, expected) => {
      expect(compose(autoDelete, edit, summary)).toBe(expected);
    });
  });

  describe('preserve', () => {
    it.each([
      // Clearing auto-delete on clean=5 (auto-delete + summary) leaves summary intact.
      [5, 1, 0, 4],
      // Setting auto-delete on clean=4 (summary only) yields 5.
      [4, 1, 1, 5],
      // Replacing only the edit bit leaves auto-delete alone.
      [1, 2, 2, 3],
      // Changes outside the mask are ignored.
      [0, 1, 4, 0],
      // No-op when the mask is empty.
      [5, 0, 7, 5],
    ])('preserve(%i, %i, %i) === %i', (existing, mask, changes, expected) => {
      expect(preserve(existing, mask, changes)).toBe(expected);
    });
  });
});
