import {
  activeHoursFires,
  compressDayRange,
  formatAxisHour,
  formatHourOption,
  formatRuleLabel,
  formatTime,
  formatTime12h,
  groupActiveHours,
  parseActiveHours,
  serializeActiveHours,
  ActiveHourEntry,
} from './active-hours.models';

describe('parseActiveHours', () => {
  it('should parse valid JSON string', () => {
    const result = parseActiveHours('[{"day":1,"hours":9,"mins":0}]');
    expect(result).toEqual([{ day: 1, hours: 9, mins: 0 }]);
  });

  it('should return [] for null', () => {
    expect(parseActiveHours(null)).toEqual([]);
  });

  it('should return [] for undefined', () => {
    expect(parseActiveHours(undefined)).toEqual([]);
  });

  it('should return [] for empty string', () => {
    expect(parseActiveHours('')).toEqual([]);
  });

  it('should return [] for malformed JSON', () => {
    expect(parseActiveHours('{not json')).toEqual([]);
  });

  it('should return [] for non-array JSON', () => {
    expect(parseActiveHours('{"day":1}')).toEqual([]);
  });

  it('should parse multiple entries', () => {
    const json = '[{"day":1,"hours":9,"mins":0},{"day":2,"hours":18,"mins":30}]';
    const result = parseActiveHours(json);
    expect(result).toHaveLength(2);
    expect(result[1]).toEqual({ day: 2, hours: 18, mins: 30 });
  });

  it('should coerce string-typed hours and mins to numbers', () => {
    const json = '[{"day":1,"hours":"09","mins":"00"},{"day":2,"hours":"18","mins":"30"}]';
    const result = parseActiveHours(json);
    expect(result).toEqual([
      { day: 1, hours: 9, mins: 0 },
      { day: 2, hours: 18, mins: 30 },
    ]);
  });

  it('should coerce string-typed day to number', () => {
    const json = '[{"day":"3","hours":"12","mins":"15"}]';
    const result = parseActiveHours(json);
    expect(result).toEqual([{ day: 3, hours: 12, mins: 15 }]);
  });
});

describe('groupActiveHours', () => {
  it('should return empty array for empty input', () => {
    expect(groupActiveHours([])).toEqual([]);
  });

  it('should group entries by same time', () => {
    const entries: ActiveHourEntry[] = [
      { day: 1, hours: 9, mins: 0 },
      { day: 2, hours: 9, mins: 0 },
      { day: 3, hours: 18, mins: 30 },
    ];
    const groups = groupActiveHours(entries);
    expect(groups).toHaveLength(2);
    expect(groups[0]).toEqual({ days: [1, 2], hours: 9, mins: 0, step: 0 });
    expect(groups[1]).toEqual({ days: [3], hours: 18, mins: 30, step: 0 });
  });

  it('should sort groups by time', () => {
    const entries: ActiveHourEntry[] = [
      { day: 1, hours: 18, mins: 0 },
      { day: 2, hours: 9, mins: 0 },
    ];
    const groups = groupActiveHours(entries);
    expect(groups[0].hours).toBe(9);
    expect(groups[1].hours).toBe(18);
  });

  it('should sort days within a group', () => {
    const entries: ActiveHourEntry[] = [
      { day: 5, hours: 9, mins: 0 },
      { day: 1, hours: 9, mins: 0 },
      { day: 3, hours: 9, mins: 0 },
    ];
    const groups = groupActiveHours(entries);
    expect(groups[0].days).toEqual([1, 3, 5]);
  });
});

describe('formatTime12h', () => {
  it('should format midnight as 12:00 AM', () => {
    expect(formatTime12h(0, 0)).toBe('12:00 AM');
  });

  it('should format morning correctly', () => {
    expect(formatTime12h(9, 30)).toBe('9:30 AM');
  });

  it('should format noon as 12:00 PM', () => {
    expect(formatTime12h(12, 0)).toBe('12:00 PM');
  });

  it('should format afternoon correctly', () => {
    expect(formatTime12h(13, 30)).toBe('1:30 PM');
  });

  it('should format 11 PM correctly', () => {
    expect(formatTime12h(23, 59)).toBe('11:59 PM');
  });

  it('should pad single-digit minutes', () => {
    expect(formatTime12h(9, 5)).toBe('9:05 AM');
  });
});

describe('compressDayRange', () => {
  it('should return "Weekdays" for [1,2,3,4,5]', () => {
    expect(compressDayRange([1, 2, 3, 4, 5])).toBe('Weekdays');
  });

  it('should return "Weekends" for [6,7]', () => {
    expect(compressDayRange([6, 7])).toBe('Weekends');
  });

  it('should return "Every day" for [1,2,3,4,5,6,7]', () => {
    expect(compressDayRange([1, 2, 3, 4, 5, 6, 7])).toBe('Every day');
  });

  it('should handle non-consecutive days', () => {
    expect(compressDayRange([1, 3, 5])).toBe('Mon, Wed, Fri');
  });

  it('should compress consecutive ranges', () => {
    expect(compressDayRange([1, 2, 3])).toBe('Mon-Wed');
  });

  it('should handle mixed consecutive and non-consecutive', () => {
    expect(compressDayRange([1, 2, 3, 6])).toBe('Mon-Wed, Sat');
  });

  it('should handle single day', () => {
    expect(compressDayRange([4])).toBe('Thu');
  });

  it('should return empty string for empty array', () => {
    expect(compressDayRange([])).toBe('');
  });

  it('should handle unsorted input', () => {
    expect(compressDayRange([7, 6])).toBe('Weekends');
  });
});

describe('range entries (issue #808)', () => {
  it('should keep end_hours/end_mins/step when parsing a range set by the bot', () => {
    const result = parseActiveHours('[{"day":1,"hours":9,"mins":0,"end_hours":17,"end_mins":0,"step":2}]');
    expect(result).toEqual([{ day: 1, endHours: 17, endMins: 0, hours: 9, mins: 0, step: 2 }]);
  });

  it('should coerce the string-typed range form PoracleNG also stores', () => {
    const result = parseActiveHours('[{"day":2,"hours":"09","mins":"00","end_hours":"17","end_mins":"30","step":"3"}]');
    expect(result).toEqual([{ day: 2, endHours: 17, endMins: 30, hours: 9, mins: 0, step: 3 }]);
  });

  it('should treat step 0 as a single fire and drop the end fields', () => {
    const result = parseActiveHours('[{"day":1,"hours":9,"mins":0,"end_hours":17,"end_mins":0,"step":0}]');
    expect(result).toEqual([{ day: 1, hours: 9, mins: 0 }]);
  });

  it('should round-trip a bot-set range back onto the wire unaltered', () => {
    const wire = '[{"day":1,"hours":9,"mins":0,"end_hours":17,"end_mins":0,"step":2}]';
    expect(serializeActiveHours(parseActiveHours(wire))).toBe(wire);
  });

  it('should emit no range fields for single-fire entries', () => {
    expect(serializeActiveHours([{ day: 1, hours: 9, mins: 0 }])).toBe('[{"day":1,"hours":9,"mins":0}]');
  });

  it('should not merge a single fire and a range that share a start time', () => {
    const groups = groupActiveHours([
      { day: 1, hours: 9, mins: 0 },
      { day: 2, endHours: 17, endMins: 0, hours: 9, mins: 0, step: 2 },
    ]);
    expect(groups).toHaveLength(2);
  });

  it('should expand a 9-17/2 range into five fires', () => {
    expect(activeHoursFires({ days: [1], endHours: 17, endMins: 0, hours: 9, mins: 0, step: 2 })).toEqual([
      [9, 0],
      [11, 0],
      [13, 0],
      [15, 0],
      [17, 0],
    ]);
  });

  it('should produce one fire when the step overshoots the end', () => {
    expect(activeHoursFires({ days: [1], endHours: 17, endMins: 0, hours: 9, mins: 0, step: 23 })).toEqual([[9, 0]]);
  });

  it('should produce one fire for a single-fire group', () => {
    expect(activeHoursFires({ days: [1], hours: 9, mins: 0, step: 0 })).toEqual([[9, 0]]);
  });

  it('should label a single fire exactly as before', () => {
    expect(formatRuleLabel({ days: [1, 2, 3, 4, 5], hours: 9, mins: 0, step: 0 }, k => k)).toBe('Weekdays 9:00 AM');
  });

  it('should label an hourly range', () => {
    const label = formatRuleLabel(
      { days: [1], endHours: 17, endMins: 0, hours: 9, mins: 0, step: 1 },
      (k, p) => (p ? `${k}|${p['days']}|${p['start']}|${p['end']}` : k),
    );
    expect(label).toBe('PROFILES.ACTIVE_HOURS_RANGE_HOURLY|Mon|9:00 AM|5:00 PM');
  });

  it('should label a stepped range', () => {
    const label = formatRuleLabel(
      { days: [1], endHours: 17, endMins: 0, hours: 9, mins: 0, step: 2 },
      (k, p) => (p ? `${k}|${p['days']}|${p['start']}|${p['end']}|${p['step']}` : k),
    );
    expect(label).toBe('PROFILES.ACTIVE_HOURS_RANGE_EVERY|Mon|9:00 AM|5:00 PM|2');
  });
});

describe('display-language formatting (UI audit D3)', () => {
  const german: Record<string, string> = {
    'PROFILES.ACTIVE_HOURS_DAY_MON': 'Mo',
    'PROFILES.ACTIVE_HOURS_DAY_TUE': 'Di',
    'PROFILES.ACTIVE_HOURS_DAY_WED': 'Mi',
    'PROFILES.ACTIVE_HOURS_DAY_SAT': 'Sa',
    'PROFILES.ACTIVE_HOURS_EVERY_DAY': 'Jeden Tag',
    'PROFILES.ACTIVE_HOURS_WEEKDAYS': 'Wochentage',
    'PROFILES.ACTIVE_HOURS_WEEKENDS': 'Wochenende',
    'PROFILES.ACTIVE_HOURS_RANGE_HOURLY': '{{days}} {{start}}–{{end}}, stündlich',
  };
  const de = (key: string, params?: Record<string, unknown>) =>
    (german[key] ?? key).replace(/\{\{(\w+)\}\}/g, (_, name: string) => String(params?.[name] ?? ''));

  it('keeps the 12-hour clock for English', () => {
    expect(formatTime(9, 0, 'en')).toBe('9:00 AM');
    expect(formatTime(17, 30, 'en')).toBe('5:30 PM');
    expect(formatTime(0, 0, 'en')).toBe(formatTime12h(0, 0));
  });

  it('uses a 24-hour clock where the language does', () => {
    expect(formatTime(17, 30, 'de')).toBe('17:30');
    expect(formatTime(0, 5, 'pl')).toBe('0:05');
    expect(formatTime(21, 0, 'fr')).toBe('21:00');
    expect(formatTime(9, 15, 'da')).toBe('9.15');
  });

  it('falls back to English for a language tag Intl rejects', () => {
    expect(formatTime(9, 0, 'not a tag!')).toBe('9:00 AM');
  });

  it('formats hour-picker options and axis ticks on the same clock', () => {
    expect(formatHourOption(18, 'en')).toBe('6 PM');
    expect(formatHourOption(18, 'de')).not.toMatch(/PM/);
    expect(formatAxisHour(0, 'en')).toBe('12 AM');
    expect(formatAxisHour(18, 'de')).toBe('18:00');
    expect(formatAxisHour(6, 'pl')).toBe('6:00');
  });

  it('translates day names and presets', () => {
    expect(compressDayRange([1, 2, 3, 4, 5], de)).toBe('Wochentage');
    expect(compressDayRange([6, 7], de)).toBe('Wochenende');
    expect(compressDayRange([1, 2, 3, 4, 5, 6, 7], de)).toBe('Jeden Tag');
    expect(compressDayRange([1, 2, 3, 6], de)).toBe('Mo-Mi, Sa');
  });

  it('falls back to the English word for a key the translator does not know', () => {
    expect(compressDayRange([4], de)).toBe('Thu');
  });

  it('labels a German rule without AM/PM or English words', () => {
    expect(formatRuleLabel({ days: [6, 7], hours: 9, mins: 30, step: 0 }, de, 'de')).toBe('Wochenende 9:30');
    expect(formatRuleLabel({ days: [1], endHours: 17, endMins: 0, hours: 9, mins: 0, step: 1 }, de, 'de')).toBe(
      'Mo 9:00–17:00, stündlich',
    );
  });
});
