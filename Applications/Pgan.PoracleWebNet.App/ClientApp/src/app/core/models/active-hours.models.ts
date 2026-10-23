export interface ActiveHourEntry {
  day: number; // 1=Monday .. 7=Sunday (ISO)
  endHours?: number; // 0-23, only meaningful when step > 0
  endMins?: number; // 0-59, only meaningful when step > 0
  hours: number; // 0-23
  mins: number; // 0-59
  /**
   * Repeat interval in hours. Absent or 0 means a single fire; > 0 makes the entry a range that fires at
   * `hours:mins`, then every `step` hours, up to and including `endHours:endMins`. Mirrors PoracleNG's
   * `db.ActiveHourEntry.IsRange()`, which is driven by `Step > 0` alone.
   */
  step?: number;
}

export interface ActiveHourGroup {
  days: number[];
  endHours?: number;
  endMins?: number;
  hours: number;
  mins: number;
  /** 0 = single fire. Required rather than optional so every construction site has to state which shape it is. */
  step: number;
}

/** Translate function shape accepted by {@link formatRuleLabel}; matches ngx-translate's `instant`. */
export type ActiveHoursTranslateFn = (key: string, params?: Record<string, unknown>) => string;

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

/** Translation keys for {@link DAY_LABELS}; the English constants stay as the fallback when a key is missing. */
export const DAY_LABEL_KEYS: Record<number, string> = {
  1: 'PROFILES.ACTIVE_HOURS_DAY_MON',
  2: 'PROFILES.ACTIVE_HOURS_DAY_TUE',
  3: 'PROFILES.ACTIVE_HOURS_DAY_WED',
  4: 'PROFILES.ACTIVE_HOURS_DAY_THU',
  5: 'PROFILES.ACTIVE_HOURS_DAY_FRI',
  6: 'PROFILES.ACTIVE_HOURS_DAY_SAT',
  7: 'PROFILES.ACTIVE_HOURS_DAY_SUN',
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
 * Groups active hour entries by identical rule shape (start time, end time and step),
 * returning sorted groups (by start time, then by first day).
 *
 * The key includes the range fields deliberately: a Monday 9:00 single fire and a Tuesday
 * 9:00-17:00/2 range are different rules, and keying on start time alone would collapse them
 * into one group so that saving rewrote both as whichever shape won.
 */
export function groupActiveHours(entries: ActiveHourEntry[]): ActiveHourGroup[] {
  const map = new Map<string, { days: number[]; group: ActiveHourGroup }>();
  for (const e of entries) {
    const step = e.step ?? 0;
    const key = `${e.hours}:${e.mins}:${e.endHours ?? ''}:${e.endMins ?? ''}:${step}`;
    let bucket = map.get(key);
    if (!bucket) {
      const group: ActiveHourGroup = { days: [], hours: e.hours, mins: e.mins, step };
      if (step > 0) {
        group.endHours = e.endHours ?? 0;
        group.endMins = e.endMins ?? 0;
      }
      bucket = { days: [], group };
      map.set(key, bucket);
    }
    if (!bucket.days.includes(e.day)) {
      bucket.days.push(e.day);
    }
  }
  const groups: ActiveHourGroup[] = [];
  for (const { days, group } of map.values()) {
    group.days = days.sort((a, b) => a - b);
    groups.push(group);
  }
  groups.sort((a, b) => a.hours * 60 + a.mins - (b.hours * 60 + b.mins));
  return groups;
}

/**
 * Expands a group into every (hours, mins) fire point it produces on one day.
 *
 * Port of PoracleNG's `ActiveHourEntry.Fires()`, including its defensive fallback: a non-positive
 * step or an end before the start yields a single fire rather than an empty list or a runaway loop.
 */
export function activeHoursFires(group: ActiveHourGroup): [number, number][] {
  const start = group.hours * 60 + group.mins;
  if (!group.step || group.step <= 0) return [[group.hours, group.mins]];
  const end = (group.endHours ?? 0) * 60 + (group.endMins ?? 0);
  const stepMins = group.step * 60;
  if (end < start) return [[group.hours, group.mins]];
  const out: [number, number][] = [];
  for (let m = start; m <= end; m += stepMins) {
    out.push([Math.floor(m / 60), m % 60]);
  }
  return out;
}

/**
 * One label for a rule, shared by the profile chip, the editor's rules list and the quest summary
 * pills so the three cannot drift. Single-fire rules keep the string they have always had.
 */
export function formatRuleLabel(group: ActiveHourGroup, translate: ActiveHoursTranslateFn, lang = 'en'): string {
  const days = compressDayRange(group.days, translate);
  const start = formatTime(group.hours, group.mins, lang);
  if (!group.step || group.step <= 0) return `${days} ${start}`;
  const end = formatTime(group.endHours ?? 0, group.endMins ?? 0, lang);
  if (group.step === 1) return translate('PROFILES.ACTIVE_HOURS_RANGE_HOURLY', { days, end, start });
  return translate('PROFILES.ACTIVE_HOURS_RANGE_EVERY', { days, end, start, step: group.step });
}

/**
 * Serialises entries into PoracleNG's on-disk shape. The range fields are snake_case and are omitted
 * entirely on single-fire entries, so payloads for existing schedules stay byte-identical.
 */
export function serializeActiveHours(entries: ActiveHourEntry[]): string {
  return JSON.stringify(
    entries.map(e => {
      const base: Record<string, number> = { day: e.day, hours: e.hours, mins: e.mins };
      if (e.step && e.step > 0) {
        base['end_hours'] = e.endHours ?? 0;
        base['end_mins'] = e.endMins ?? 0;
        base['step'] = e.step;
      }
      return base;
    }),
  );
}

/**
 * Formats a wall-clock time the way the display language writes it: "9:00 AM" in English, "9:00" in
 * German, "9.00" in Danish. The time is a schedule rule, not an instant, so it is formatted in UTC to
 * keep the viewer's own time zone and DST out of it.
 */
export function formatTime(hours: number, mins: number, lang = 'en'): string {
  return normaliseSpaces(timeFormat(lang, { hour: 'numeric', minute: '2-digit' }).format(wallClock(hours, mins)));
}

/** One option of an hour picker: "9 AM" where the language uses a 12-hour clock, "09" or "09 Uhr" otherwise. */
export function formatHourOption(hours: number, lang = 'en'): string {
  return normaliseSpaces(timeFormat(lang, { hour: 'numeric' }).format(wallClock(hours, 0)));
}

/** One tick of the weekly preview's axis: "6 AM" on a 12-hour clock, "6:00" on a 24-hour one. */
export function formatAxisHour(hours: number, lang = 'en'): string {
  return uses12HourClock(lang) ? formatHourOption(hours, lang) : formatTime(hours, 0, lang);
}

export function uses12HourClock(lang = 'en'): boolean {
  return timeFormat(lang, { hour: 'numeric' }).resolvedOptions().hour12 === true;
}

function timeFormat(lang: string, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  try {
    return new Intl.DateTimeFormat(lang || 'en', { ...options, timeZone: 'UTC' });
  } catch {
    // A malformed language tag throws RangeError; English is the app's own fallback language.
    return new Intl.DateTimeFormat('en', { ...options, timeZone: 'UTC' });
  }
}

function wallClock(hours: number, mins: number): Date {
  return new Date(Date.UTC(2024, 0, 1, hours, mins));
}

/** ICU puts a narrow no-break space before AM/PM; plain spaces keep labels comparable and copyable. */
function normaliseSpaces(value: string): string {
  return value.replace(/[\u00a0\u202f]/g, ' ');
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
 * consecutive runs -> "Mon-Wed", singletons -> "Mon". With `translate`, every word comes from the
 * display language; a key it cannot resolve falls back to the English word rather than showing the key.
 */
export function compressDayRange(days: number[], translate?: ActiveHoursTranslateFn): string {
  if (days.length === 0) return '';
  const sorted = [...days].sort((a, b) => a - b);
  const word = (key: string, english: string) => {
    if (!translate) return english;
    const value = translate(key);
    return value && value !== key ? value : english;
  };
  const day = (d: number) => word(DAY_LABEL_KEYS[d], DAY_LABELS[d]);

  // Check for well-known sets
  if (sorted.length === 7) return word('PROFILES.ACTIVE_HOURS_EVERY_DAY', 'Every day');
  if (sorted.length === 5 && sorted[0] === 1 && sorted[4] === 5) return word('PROFILES.ACTIVE_HOURS_WEEKDAYS', 'Weekdays');
  if (sorted.length === 2 && sorted[0] === 6 && sorted[1] === 7) return word('PROFILES.ACTIVE_HOURS_WEEKENDS', 'Weekends');

  // Build ranges
  const ranges: string[] = [];
  let start = sorted[0];
  let end = sorted[0];
  for (let i = 1; i < sorted.length; i++) {
    if (sorted[i] === end + 1) {
      end = sorted[i];
    } else {
      ranges.push(start === end ? day(start) : `${day(start)}-${day(end)}`);
      start = sorted[i];
      end = sorted[i];
    }
  }
  ranges.push(start === end ? day(start) : `${day(start)}-${day(end)}`);
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
      .map((e: Record<string, unknown>) => {
        const entry: ActiveHourEntry = {
          day: Number(e['day']),
          hours: Number(e['hours']),
          mins: Number(e['mins']),
        };
        // step > 0 is what makes an entry a range upstream; anything else (absent, 0, negative,
        // unparseable) is a single fire, and the end fields alongside it are ignored exactly as
        // PoracleNG's Fires() ignores them.
        const step = Number(e['step']);
        if (!isNaN(step) && step > 0) {
          entry.step = step;
          entry.endHours = toNumberOrZero(e['end_hours']);
          entry.endMins = toNumberOrZero(e['end_mins']);
        }
        return entry;
      })
      .filter((e: ActiveHourEntry) => !isNaN(e.day) && !isNaN(e.hours) && !isNaN(e.mins));
  } catch {
    return [];
  }
}

function toNumberOrZero(value: unknown): number {
  const n = Number(value);
  return isNaN(n) ? 0 : n;
}
