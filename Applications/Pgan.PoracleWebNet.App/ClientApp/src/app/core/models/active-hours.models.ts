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
export function formatRuleLabel(group: ActiveHourGroup, translate: ActiveHoursTranslateFn): string {
  const days = compressDayRange(group.days);
  const start = formatTime12h(group.hours, group.mins);
  if (!group.step || group.step <= 0) return `${days} ${start}`;
  const end = formatTime12h(group.endHours ?? 0, group.endMins ?? 0);
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
