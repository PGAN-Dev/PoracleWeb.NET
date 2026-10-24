import { DriftDescribeContext, describeDrift, distanceKm, sameLevelNames } from './pair-drift';
import { DriftItem, ReverseGeocodingResult } from '../../core/models';

/** Echoes the key and its params, so a test can read which sentence was chosen and what filled it. */
function t(key: string, params?: Record<string, unknown>): string {
  return params ? `${key} ${JSON.stringify(params)}` : key;
}

function ctx(over: Partial<DriftDescribeContext> = {}): DriftDescribeContext {
  return {
    side: s => (s === 'discord' ? 'Discord' : 'Telegram'),
    area: key => key.toUpperCase(),
    geo: () => undefined,
    lang: 'en',
    t,
    ...over,
  };
}

const geo = (r: Partial<ReverseGeocodingResult>) => r as ReverseGeocodingResult;

describe('describeDrift', () => {
  describe('location', () => {
    const item: DriftItem = {
      discord: { lat: 51.5, lon: -0.12 },
      field: 'location',
      fingerprint: 'f',
      telegram: { lat: 55.86, lon: -4.25 },
    };

    it('names both sides at the same level and says how far apart they are', () => {
      const answers: Record<string, ReverseGeocodingResult> = {
        '51.5': geo({ city: 'London', country: 'United Kingdom' }),
        '55.86': geo({ country: 'United Kingdom', town: 'Glasgow' }),
      };
      const line = describeDrift(item, ctx({ geo: p => answers[String(p.lat)] }));

      expect(line.labelKey).toBe('DRIFT.LABEL_LOCATION');
      expect(line.text).toContain('DRIFT.LOCATION ');
      expect(line.text).toContain('"discord":"London"');
      expect(line.text).toContain('"telegram":"Glasgow"');
      expect(line.text).toContain('"distance":"556"');
    });

    it('never names one side by town and the other by country', () => {
      expect(sameLevelNames(geo({ city: 'Richmond', country: 'United Kingdom' }), geo({ country: 'United Kingdom' }))).toEqual([
        'United Kingdom',
        'United Kingdom',
      ]);
    });

    it('says only the distance, never raw coordinates, when a place has no name', () => {
      const line = describeDrift({ ...item, telegram: { lat: 0, lon: -30 } }, ctx());

      expect(line.text).toContain('DRIFT.LOCATION_APART');
      expect(line.text).not.toContain('-30');
    });

    it('says which side has no location at all', () => {
      const line = describeDrift({ ...item, telegram: { lat: 0, lon: 0 } }, ctx());

      expect(line.text).toContain('DRIFT.LOCATION_UNSET');
      expect(line.text).toContain('"set":"Discord"');
    });

    it('says what each choice does to the other app', () => {
      const line = describeDrift(item, ctx());

      expect(line.consequence.discord).toContain('"other":"Telegram"');
      expect(line.consequence.telegram).toContain('"other":"Discord"');
      expect(line.removes).toEqual({ discord: [], telegram: [] });
    });

    it('measures great-circle distance', () => {
      expect(Math.round(distanceKm({ lat: 0, lon: 0 }, { lat: 0, lon: 1 }))).toBe(111);
    });
  });

  describe('areas', () => {
    const item: DriftItem = { discord: ['aclt', 'old town'], field: 'areas', fingerprint: 'f', telegram: ['Old_Town', 'riverside'] };

    it('is labelled, and names the areas only one side has through the display names', () => {
      const line = describeDrift(item, ctx());

      expect(line.labelKey).toBe('DRIFT.LABEL_AREAS');
      expect(line.text).toContain('"names":"ACLT","side":"Discord"');
      expect(line.text).toContain('"names":"RIVERSIDE","side":"Telegram"');
      expect(line.text).not.toContain('OLD TOWN');
    });

    it('says what the other app gains and stops using', () => {
      const line = describeDrift(item, ctx());

      expect(line.consequence.discord).toContain('DRIFT.CONSEQ_GETS {"names":"ACLT","other":"Telegram"}');
      expect(line.consequence.discord).toContain('DRIFT.CONSEQ_AREAS_LOSES {"names":"RIVERSIDE","other":"Telegram"}');
      // Areas are not deleted from anything, so nothing needs confirming.
      expect(line.removes.discord).toEqual([]);
    });
  });

  describe('active profile', () => {
    const item: DriftItem = {
      details: { discord: { name: 'Weekend', otherHas: false }, telegram: { name: 'Home', otherHas: true } },
      discord: 2,
      field: 'current_profile',
      fingerprint: 'f',
      telegram: 1,
    };

    it('names the profiles, not their numbers', () => {
      const line = describeDrift(item, ctx());

      expect(line.text).toBe('DRIFT.PROFILE_CURRENT {"discord":"Weekend","telegram":"Home"}');
    });

    it('blocks the side whose profile the other app lacks, and says why', () => {
      const line = describeDrift(item, ctx());

      expect(line.blocked).toEqual({ discord: true, telegram: false });
      expect(line.consequence.discord).toContain('DRIFT.CONSEQ_PROFILE_MISSING');
      expect(line.consequence.telegram).toContain('DRIFT.CONSEQ_SWITCH {"name":"Home","other":"Discord"}');
    });

    it('falls back to the number without details, and blocks nothing it cannot judge', () => {
      const line = describeDrift({ discord: 2, field: 'current_profile', fingerprint: 'f', telegram: 1 }, ctx());

      expect(line.text).toContain('DRIFT.PROFILE_NAMELESS {\\"no\\":2}');
      expect(line.blocked).toEqual({ discord: false, telegram: false });
    });
  });

  describe('profiles', () => {
    const item: DriftItem = {
      discord: [
        { name: 'Home', active_hours: [], areas: null, lat: null, lon: null, no: 1 },
        { name: 'Work', active_hours: [{ day: 1, hours: 8, mins: 0 }], areas: ['old town'], lat: 1, lon: 1, no: 2 },
        { name: 'Weekend', active_hours: [], areas: [], lat: 1, lon: 1, no: 3 },
      ],
      field: 'profiles',
      fingerprint: 'f',
      telegram: [
        { name: 'Home', active_hours: [], areas: null, lat: null, lon: null, no: 1 },
        { name: 'Office', active_hours: [], areas: ['riverside'], lat: 1, lon: 1, no: 2 },
      ],
    };

    it('names the profile only one side has and what differs in the shared ones', () => {
      const line = describeDrift(item, ctx());

      expect(line.text).toContain('DRIFT.PROFILES_EXTRA {"names":"Weekend","side":"Discord"}');
      expect(line.text).toContain('Work / Office');
      expect(line.text).toContain('DRIFT.DIFF_NAME');
      expect(line.text).toContain('DRIFT.DIFF_SCHEDULE');
      expect(line.text).toContain('DRIFT.DIFF_AREAS');
      expect(line.text).not.toContain('Home');
    });

    it('lists what keeping a side deletes on the other app', () => {
      const line = describeDrift(item, ctx());

      expect(line.removes.telegram).toEqual(['Weekend']);
      expect(line.removes.discord).toEqual([]);
      expect(line.consequence.telegram).toContain('DRIFT.CONSEQ_DELETES {"names":"Weekend","other":"Discord"}');
      expect(line.consequence.discord).toContain('DRIFT.CONSEQ_GETS {"names":"Weekend","other":"Telegram"}');
      expect(line.consequence.discord).toContain('DRIFT.CONSEQ_PROFILES_MATCH {"names":"Work","other":"Telegram"}');
    });
  });

  describe('saved places', () => {
    const item: DriftItem = {
      details: { labels: { 'aclt gym': 'ACLT Gym', home: 'Home', "mum's": "Mum's" } },
      discord: [
        { label: 'home', lat: 1, lon: 1 },
        { label: "mum's", lat: 2, lon: 2 },
      ],
      field: 'places',
      fingerprint: 'f',
      telegram: [
        { label: 'aclt gym', lat: 3, lon: 3 },
        { label: 'home', lat: 1.5, lon: 1 },
      ],
    };

    it('names the places as typed, and the ones in different spots', () => {
      const line = describeDrift(item, ctx());

      expect(line.labelKey).toBe('DRIFT.LABEL_PLACES');
      expect(line.text).toContain('"names":"Mum\'s","side":"Discord"');
      expect(line.text).toContain('"names":"ACLT Gym","side":"Telegram"');
      expect(line.text).toContain('DRIFT.PLACES_MOVED {"names":"Home"}');
    });

    it('lists the places keeping a side deletes on the other app', () => {
      const line = describeDrift(item, ctx());

      expect(line.removes.discord).toEqual(['ACLT Gym']);
      expect(line.removes.telegram).toEqual(["Mum's"]);
    });
  });
});
