import { compressDayRange, formatTime12h, groupActiveHours, parseActiveHours, ActiveHourEntry } from './active-hours.models';

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
    expect(groups[0]).toEqual({ days: [1, 2], hours: 9, mins: 0 });
    expect(groups[1]).toEqual({ days: [3], hours: 18, mins: 30 });
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
