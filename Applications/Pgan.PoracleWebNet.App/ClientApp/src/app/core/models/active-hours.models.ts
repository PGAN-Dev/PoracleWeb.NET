export interface ActiveHourEntry {
  day: number; // 1=Monday .. 7=Sunday (ISO)
  hours: number; // 0-23
  mins: number; // 0-59
}

export interface ActiveHourGroup {
  days: number[];
  hours: number;
  mins: number;
}

export interface ProfileSchedule {
  activeHours: ActiveHourEntry[];
  color: string;
  name: string;
  profileNo: number;
}

export const DAY_LABELS: Record<number, string> = {
  1: 'Mon',
  2: 'Tue',
  3: 'Wed',
  4: 'Thu',
  5: 'Fri',
  6: 'Sat',
  7: 'Sun',
};

export const DAY_LETTERS: Record<number, string> = {
  1: 'M',
  2: 'T',
  3: 'W',
  4: 'T',
  5: 'F',
  6: 'S',
  7: 'S',
};

/**
 * Groups active hour entries by identical time (hours + mins),
 * returning sorted groups (by time, then by first day).
 */
export function groupActiveHours(entries: ActiveHourEntry[]): ActiveHourGroup[] {
  const map = new Map<string, number[]>();
  for (const e of entries) {
    const key = `${e.hours}:${e.mins}`;
    const days = map.get(key) ?? [];
    if (!days.includes(e.day)) {
      days.push(e.day);
    }
    map.set(key, days);
  }
  const groups: ActiveHourGroup[] = [];
  for (const [key, days] of map) {
    const [h, m] = key.split(':').map(Number);
    groups.push({ days: days.sort((a, b) => a - b), hours: h, mins: m });
  }
  groups.sort((a, b) => a.hours * 60 + a.mins - (b.hours * 60 + b.mins));
  return groups;
}

/** Formats hours and minutes as 12-hour time, e.g. "9:00 AM". */
export function formatTime12h(hours: number, mins: number): string {
  const period = hours >= 12 ? 'PM' : 'AM';
  const h = hours % 12 || 12;
  const m = mins.toString().padStart(2, '0');
  return `${h}:${m} ${period}`;
}

/**
 * Compresses a sorted array of ISO day numbers into a human-readable string.
 * [1,2,3,4,5] -> "Weekdays", [6,7] -> "Weekends", [1,2,3,4,5,6,7] -> "Every day",
 * consecutive runs -> "Mon-Wed", singletons -> "Mon".
 */
export function compressDayRange(days: number[]): string {
  if (days.length === 0) return '';
  const sorted = [...days].sort((a, b) => a - b);

  // Check for well-known sets
  if (sorted.length === 7) return 'Every day';
  if (sorted.length === 5 && sorted[0] === 1 && sorted[4] === 5) return 'Weekdays';
  if (sorted.length === 2 && sorted[0] === 6 && sorted[1] === 7) return 'Weekends';

  // Build ranges
  const ranges: string[] = [];
  let start = sorted[0];
  let end = sorted[0];
  for (let i = 1; i < sorted.length; i++) {
    if (sorted[i] === end + 1) {
      end = sorted[i];
    } else {
      ranges.push(start === end ? DAY_LABELS[start] : `${DAY_LABELS[start]}-${DAY_LABELS[end]}`);
      start = sorted[i];
      end = sorted[i];
    }
  }
  ranges.push(start === end ? DAY_LABELS[start] : `${DAY_LABELS[start]}-${DAY_LABELS[end]}`);
  return ranges.join(', ');
}

/** Safely parses a JSON string to ActiveHourEntry[], returning [] on failure.
 *  PoracleNG stores hours/mins as strings ("09","00") so we coerce to numbers. */
export function parseActiveHours(json: string | null | undefined): ActiveHourEntry[] {
  if (!json) return [];
  try {
    const parsed = JSON.parse(json);
    if (!Array.isArray(parsed)) return [];
    return parsed
      .filter((e: unknown) => typeof e === 'object' && e !== null && 'day' in e && 'hours' in e && 'mins' in e)
      .map((e: Record<string, unknown>) => ({
        day: Number(e['day']),
        hours: Number(e['hours']),
        mins: Number(e['mins']),
      }))
      .filter((e: ActiveHourEntry) => !isNaN(e.day) && !isNaN(e.hours) && !isNaN(e.mins));
  } catch {
    return [];
  }
}
