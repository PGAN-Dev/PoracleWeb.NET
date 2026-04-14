import { LEADER_GRUNT_TYPES, hasNoGenderVariants, isEventType } from './invasion.constants';

describe('invasion.constants', () => {
  describe('LEADER_GRUNT_TYPES', () => {
    it('contains the four Rocket bosses', () => {
      expect(LEADER_GRUNT_TYPES).toEqual(new Set(['cliff', 'arlo', 'sierra', 'giovanni']));
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

  describe('isEventType', () => {
    it('does not misclassify leaders as events', () => {
      for (const leader of LEADER_GRUNT_TYPES) {
        expect(isEventType(leader)).toBe(false);
      }
    });
  });
});
