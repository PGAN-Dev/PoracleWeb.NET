import {
  NO_GENDER_GRUNT_TYPES,
  getDisplayName,
  getGruntIconUrl,
  hasNoGenderVariants,
  isEventType,
  isGenderFixed,
} from './invasion.constants';

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
  });

  describe('getGruntIconUrl gender-aware variants', () => {
    it('picks invasion/4 for male mixed and invasion/5 for female mixed', () => {
      expect(getGruntIconUrl('mixed', 1)).toContain('/invasion/4.png');
      expect(getGruntIconUrl('mixed', 2)).toContain('/invasion/5.png');
    });

    it('picks invasion/45 for male decoy and invasion/46 for female decoy', () => {
      expect(getGruntIconUrl('decoy', 1)).toContain('/invasion/45.png');
      expect(getGruntIconUrl('decoy', 2)).toContain('/invasion/46.png');
    });

    it('falls back to the generic id when gender is omitted (decoy defaults to female — male does not spawn in-game)', () => {
      expect(getGruntIconUrl('mixed')).toContain('/invasion/4.png');
      expect(getGruntIconUrl('decoy')).toContain('/invasion/46.png');
    });

    it('ignores gender for grunts without gender-specific icons', () => {
      expect(getGruntIconUrl('cliff', 1)).toContain('/invasion/41.png');
      expect(getGruntIconUrl('cliff', 2)).toContain('/invasion/41.png');
    });
  });

  describe('getDisplayName gender suffix', () => {
    it('appends (Male)/(Female) for mixed and decoy', () => {
      expect(getDisplayName('mixed', 1)).toBe('Mixed Grunt (Male)');
      expect(getDisplayName('mixed', 2)).toBe('Mixed Grunt (Female)');
      expect(getDisplayName('decoy', 1)).toBe('Decoy Grunt (Male)');
      expect(getDisplayName('decoy', 2)).toBe('Decoy Grunt (Female)');
    });

    it('omits the suffix when gender is 0 or undefined', () => {
      expect(getDisplayName('mixed', 0)).toBe('Mixed Grunt');
      expect(getDisplayName('mixed')).toBe('Mixed Grunt');
    });

    it('does not append a suffix for non-split grunt types', () => {
      expect(getDisplayName('fire', 1)).toBe('Fire');
      expect(getDisplayName('cliff', 2)).toBe('Cliff');
    });
  });

  describe('getGruntIconUrl typed grunt gender variants (#224)', () => {
    it('picks the male InvasionCharacter id when gender is Male for typed grunts', () => {
      expect(getGruntIconUrl('water', 1)).toContain('/invasion/39.png');
      expect(getGruntIconUrl('bug', 1)).toContain('/invasion/7.png');
      expect(getGruntIconUrl('fire', 1)).toContain('/invasion/19.png');
    });

    it('picks the female InvasionCharacter id when gender is Female for typed grunts', () => {
      expect(getGruntIconUrl('water', 2)).toContain('/invasion/38.png');
      expect(getGruntIconUrl('bug', 2)).toContain('/invasion/6.png');
      expect(getGruntIconUrl('fire', 2)).toContain('/invasion/18.png');
    });

    it('falls back to the Pokémon type badge when gender is Any for typed grunts', () => {
      expect(getGruntIconUrl('water', 0)).toContain('/type/11.png');
      expect(getGruntIconUrl('bug')).toContain('/type/7.png');
    });
  });
});
