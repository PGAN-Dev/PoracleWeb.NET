import {
  EVENT_TYPE_INFO,
  GRUNT_DISPLAY_KEYS,
  NO_GENDER_GRUNT_TYPES,
  getGruntDisplayKey,
  getGruntDisplayName,
  getGruntIconPath,
  hasNoGenderVariants,
  isEventType,
  isGenderFixed,
} from './invasion.constants';

const identityTranslate = (key: string): string => key;

describe('invasion.constants', () => {
  describe('NO_GENDER_GRUNT_TYPES', () => {
    it('contains the four Rocket bosses', () => {
      expect(NO_GENDER_GRUNT_TYPES).toEqual(new Set(['cliff', 'arlo', 'sierra', 'giovanni']));
    });
  });

  describe('hasNoGenderVariants', () => {
    it.each(['cliff', 'arlo', 'sierra', 'giovanni'])('returns true for leader %s', leader => {
      expect(hasNoGenderVariants(leader)).toBe(true);
    });

    it.each(['kecleon', 'gold-stop', 'showcase'])('returns true for event type %s', event => {
      expect(hasNoGenderVariants(event)).toBe(true);
    });

    it.each(['mixed', 'fire', 'water', 'darkness', 'decoy'])('returns false for gendered grunt %s', grunt => {
      expect(hasNoGenderVariants(grunt)).toBe(false);
    });

    it('returns false for null and empty string', () => {
      expect(hasNoGenderVariants(null)).toBe(false);
      expect(hasNoGenderVariants('')).toBe(false);
    });
  });

  describe('isGenderFixed', () => {
    it.each(['cliff', 'arlo', 'sierra', 'giovanni'])('returns true for leader %s', leader => {
      expect(isGenderFixed(leader)).toBe(true);
    });

    it.each(['mixed', 'decoy'])('returns true for gender-split grunt %s', type => {
      expect(isGenderFixed(type)).toBe(true);
    });

    it.each(['kecleon', 'gold-stop'])('returns true for event type %s', type => {
      expect(isGenderFixed(type)).toBe(true);
    });

    it.each(['bug', 'fire', 'water', 'darkness'])('returns false for typed grunt %s', type => {
      expect(isGenderFixed(type)).toBe(false);
    });
  });

  describe('isEventType', () => {
    it('does not misclassify leaders as events', () => {
      for (const leader of NO_GENDER_GRUNT_TYPES) {
        expect(isEventType(leader)).toBe(false);
      }
    });

    it('identifies event grunts', () => {
      expect(isEventType('kecleon')).toBe(true);
      expect(isEventType('showcase')).toBe(true);
      expect(isEventType(null)).toBe(false);
    });
  });

  describe('getGruntIconPath gender-aware variants', () => {
    it('picks invasion/4 for male mixed and invasion/5 for female mixed', () => {
      expect(getGruntIconPath('mixed', 1)).toBe('invasion/4.png');
      expect(getGruntIconPath('mixed', 2)).toBe('invasion/5.png');
    });

    it('picks invasion/45 for male decoy and invasion/46 for female decoy', () => {
      expect(getGruntIconPath('decoy', 1)).toBe('invasion/45.png');
      expect(getGruntIconPath('decoy', 2)).toBe('invasion/46.png');
    });

    it('falls back to the generic id when gender is omitted (decoy defaults to female — male does not spawn in-game)', () => {
      expect(getGruntIconPath('mixed')).toBe('invasion/4.png');
      expect(getGruntIconPath('decoy')).toBe('invasion/46.png');
    });

    it('ignores gender for grunts without gender-specific icons', () => {
      expect(getGruntIconPath('cliff', 1)).toBe('invasion/41.png');
      expect(getGruntIconPath('cliff', 2)).toBe('invasion/41.png');
    });

    it('resolves typed grunts to /type/ icons', () => {
      expect(getGruntIconPath('bug')).toBe('type/7.png');
    });

    it('falls back to /invasion/0.png for null/unmapped', () => {
      expect(getGruntIconPath(null)).toBe('invasion/0.png');
      expect(getGruntIconPath('notarealtype')).toBe('invasion/0.png');
    });
  });

  describe('getGruntDisplayKey', () => {
    it('returns EVERYTHING key for null/empty input', () => {
      expect(getGruntDisplayKey(null)).toBe('INVASIONS.GRUNT_TYPES.EVERYTHING');
      expect(getGruntDisplayKey('')).toBe('INVASIONS.GRUNT_TYPES.EVERYTHING');
    });

    it('returns event displayKey for event types', () => {
      expect(getGruntDisplayKey('kecleon')).toBe('INVASIONS.EVENT_TYPES.KECLEON');
      expect(getGruntDisplayKey('gold-stop')).toBe('INVASIONS.EVENT_TYPES.GOLD_STOP');
      expect(getGruntDisplayKey('showcase')).toBe('INVASIONS.EVENT_TYPES.SHOWCASE');
    });

    it('returns GRUNT_TYPES key for typed grunts and leaders', () => {
      expect(getGruntDisplayKey('bug')).toBe('INVASIONS.GRUNT_TYPES.BUG');
      expect(getGruntDisplayKey('metal')).toBe('INVASIONS.GRUNT_TYPES.METAL');
      expect(getGruntDisplayKey('mixed')).toBe('INVASIONS.GRUNT_TYPES.MIXED');
      expect(getGruntDisplayKey('giovanni')).toBe('INVASIONS.GRUNT_TYPES.GIOVANNI');
    });

    it('falls back to UNKNOWN_GRUNT for unmapped grunt types', () => {
      expect(getGruntDisplayKey('notarealtype')).toBe('INVASIONS.UNKNOWN_GRUNT');
    });

    it('has a display key for every canonical grunt_type', () => {
      const allKnown = [
        'bug',
        'dark',
        'dragon',
        'electric',
        'fairy',
        'fighting',
        'fire',
        'flying',
        'ghost',
        'grass',
        'ground',
        'ice',
        'metal',
        'normal',
        'poison',
        'psychic',
        'rock',
        'water',
        'mixed',
        'darkness',
        'decoy',
        'cliff',
        'arlo',
        'sierra',
        'giovanni',
      ];
      for (const key of allKnown) {
        expect(GRUNT_DISPLAY_KEYS[key]).toMatch(/^INVASIONS\.GRUNT_TYPES\./);
      }
    });
  });

  describe('getGruntDisplayName', () => {
    it('passes the base key through the translator', () => {
      expect(getGruntDisplayName('bug', undefined, identityTranslate)).toBe('INVASIONS.GRUNT_TYPES.BUG');
    });

    it('appends gender suffix for gendered grunts (mixed/decoy)', () => {
      expect(getGruntDisplayName('mixed', 1, identityTranslate)).toBe('INVASIONS.GRUNT_TYPES.MIXED INVASIONS.GENDER_SUFFIX_MALE');
      expect(getGruntDisplayName('mixed', 2, identityTranslate)).toBe('INVASIONS.GRUNT_TYPES.MIXED INVASIONS.GENDER_SUFFIX_FEMALE');
      expect(getGruntDisplayName('decoy', 1, identityTranslate)).toBe('INVASIONS.GRUNT_TYPES.DECOY INVASIONS.GENDER_SUFFIX_MALE');
    });

    it('omits gender suffix when gender is undefined or 0', () => {
      expect(getGruntDisplayName('mixed', undefined, identityTranslate)).toBe('INVASIONS.GRUNT_TYPES.MIXED');
      expect(getGruntDisplayName('mixed', 0, identityTranslate)).toBe('INVASIONS.GRUNT_TYPES.MIXED');
    });

    it('never appends gender suffix for non-gendered grunts', () => {
      expect(getGruntDisplayName('bug', 1, identityTranslate)).toBe('INVASIONS.GRUNT_TYPES.BUG');
      expect(getGruntDisplayName('giovanni', 2, identityTranslate)).toBe('INVASIONS.GRUNT_TYPES.GIOVANNI');
    });
  });

  describe('EVENT_TYPE_INFO', () => {
    it('every entry has a displayKey pointing to INVASIONS.EVENT_TYPES.*', () => {
      for (const info of Object.values(EVENT_TYPE_INFO)) {
        expect(info.displayKey).toMatch(/^INVASIONS\.EVENT_TYPES\./);
      }
    });
  });

  describe('getGruntIconPath typed grunt gender variants (#224)', () => {
    it('picks the male InvasionCharacter id when gender is Male for typed grunts', () => {
      expect(getGruntIconPath('water', 1)).toBe('invasion/39.png');
      expect(getGruntIconPath('bug', 1)).toBe('invasion/7.png');
      expect(getGruntIconPath('fire', 1)).toBe('invasion/19.png');
    });

    it('picks the female InvasionCharacter id when gender is Female for typed grunts', () => {
      expect(getGruntIconPath('water', 2)).toBe('invasion/38.png');
      expect(getGruntIconPath('bug', 2)).toBe('invasion/6.png');
      expect(getGruntIconPath('fire', 2)).toBe('invasion/18.png');
    });

    it('falls back to the Pokémon type badge when gender is Any for typed grunts', () => {
      expect(getGruntIconPath('water', 0)).toBe('type/11.png');
      expect(getGruntIconPath('bug')).toBe('type/7.png');
    });
  });
});
