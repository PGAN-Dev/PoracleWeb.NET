import {
  ANY_LEVEL,
  ANY_LEVEL_VALUE,
  EGG_LEVELS,
  isKnownLevel,
  KNOWN_LEVELS,
  makeCustomLevel,
  MEGA_LEVELS,
  OVERFLOW_RAID_LEVELS,
  PRIMARY_RAID_LEVELS,
  resolveLevel,
  STAR_LEVELS,
} from './raid-level.models';

describe('raid-level.models (canonical 19 levels)', () => {
  describe('KNOWN_LEVELS', () => {
    it('has 19 entries covering integers 1-19 in order', () => {
      expect(KNOWN_LEVELS.length).toBe(19);
      KNOWN_LEVELS.forEach((opt, i) => expect(opt.value).toBe(i + 1));
    });

    it('partitions correctly by category', () => {
      const byCategory = KNOWN_LEVELS.reduce<Record<string, number[]>>((acc, l) => {
        (acc[l.category] ||= []).push(l.value);
        return acc;
      }, {});
      expect(byCategory['star']).toEqual([1, 2, 3, 4, 5]);
      expect(byCategory['mega']).toEqual([6, 7]);
      expect(byCategory['special']).toEqual([8, 9, 10]);
      expect(byCategory['shadow']).toEqual([11, 12, 13, 14, 15]);
      expect(byCategory['superMega']).toEqual([16, 17]);
      expect(byCategory['coordinated']).toEqual([18, 19]);
    });

    it('points level 7 at Mega Legendary, level 9 at Elite (fixes prior mislabel)', () => {
      const seven = KNOWN_LEVELS.find(l => l.value === 7)!;
      const nine = KNOWN_LEVELS.find(l => l.value === 9)!;
      expect(seven.labelKey).toBe('RAIDS.LEVEL.RAID_7');
      expect(seven.category).toBe('mega');
      expect(nine.labelKey).toBe('RAIDS.LEVEL.RAID_9');
      expect(nine.category).toBe('special');
    });

    it('every entry uses the RAID_N label key', () => {
      KNOWN_LEVELS.forEach(opt => {
        expect(opt.labelKey).toBe(`RAIDS.LEVEL.RAID_${opt.value}`);
      });
    });
  });

  describe('derived groupings', () => {
    it('STAR_LEVELS is 1-5', () => {
      expect(STAR_LEVELS.map(l => l.value)).toEqual([1, 2, 3, 4, 5]);
    });

    it('EGG_LEVELS mirrors STAR_LEVELS (eggs only have star tiers)', () => {
      expect(EGG_LEVELS.map(l => l.value)).toEqual([1, 2, 3, 4, 5]);
    });

    it('MEGA_LEVELS is 6-7', () => {
      expect(MEGA_LEVELS.map(l => l.value)).toEqual([6, 7]);
    });

    it('PRIMARY_RAID_LEVELS is 1-7', () => {
      expect(PRIMARY_RAID_LEVELS.map(l => l.value)).toEqual([1, 2, 3, 4, 5, 6, 7]);
    });

    it('OVERFLOW_RAID_LEVELS is 8-19', () => {
      expect(OVERFLOW_RAID_LEVELS.map(l => l.value)).toEqual([8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19]);
    });
  });

  describe('resolveLevel', () => {
    it('returns ANY_LEVEL for the 9000 sentinel', () => {
      expect(resolveLevel(ANY_LEVEL_VALUE)).toEqual(ANY_LEVEL);
    });

    it('returns the canonical option for any value 1-19', () => {
      for (let v = 1; v <= 19; v++) {
        const opt = resolveLevel(v);
        expect(opt.value).toBe(v);
        expect(opt.labelKey).toBe(`RAIDS.LEVEL.RAID_${v}`);
        expect(opt.category).not.toBe('custom');
      }
    });

    it('returns a custom option for unrecognized values (20+, negatives, 0)', () => {
      expect(resolveLevel(42).category).toBe('custom');
      expect(resolveLevel(20).category).toBe('custom');
      expect(resolveLevel(0).category).toBe('custom');
      expect(resolveLevel(-1).category).toBe('custom');
    });
  });

  describe('isKnownLevel', () => {
    it('is true for 1-19 and 9000', () => {
      for (let v = 1; v <= 19; v++) expect(isKnownLevel(v)).toBe(true);
      expect(isKnownLevel(ANY_LEVEL_VALUE)).toBe(true);
    });

    it('is false for 0, negatives, and 20+', () => {
      expect(isKnownLevel(0)).toBe(false);
      expect(isKnownLevel(-3)).toBe(false);
      expect(isKnownLevel(20)).toBe(false);
      expect(isKnownLevel(42)).toBe(false);
    });
  });

  describe('makeCustomLevel', () => {
    it('round-trips through resolveLevel', () => {
      expect(resolveLevel(66)).toEqual(makeCustomLevel(66));
    });
  });
});
