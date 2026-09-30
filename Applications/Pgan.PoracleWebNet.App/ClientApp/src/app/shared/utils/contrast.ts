/**
 * Colours for a filled chip whose hue comes from data (a generation, an alarm type, a reward type),
 * so its label stays readable. WCAG AA asks 4.5:1 of small text, and white on most Material 500
 * shades misses it: #4caf50 manages 2.8:1, #ffd600 1.4:1.
 *
 * Light, saturated fills (yellows, amber, orange, cyan, light green) keep their hue and take dark text,
 * which they carry at 7:1 or better. Everything else keeps white text on the same hue, darkened only as
 * far as it takes to clear the bar, so a green chip stays recognisably green.
 */
export interface ChipColors {
  background: string;
  color: string;
}

export const CHIP_DARK_TEXT = '#212121';
const WHITE: Rgb = [255, 255, 255];
const DARK: Rgb = [0x21, 0x21, 0x21];
/** A hair over 4.5 so rounding in a checker can never land it under. */
const TARGET = 4.6;
/** Dark text only where it is comfortably better, not merely passing. */
const DARK_TEXT_THRESHOLD = 7;

type Rgb = [number, number, number];

export function contrastRatio(a: string, b: string): number {
  const ca = parseHex(a);
  const cb = parseHex(b);
  if (!ca || !cb) return 1;
  return ratio(ca, cb);
}

export function readableChip(background: string): ChipColors {
  const bg = parseHex(background);
  if (!bg) return { background, color: '#fff' };
  if (ratio(bg, WHITE) >= TARGET) return { background, color: '#fff' };
  if (ratio(bg, DARK) >= DARK_TEXT_THRESHOLD) return { background, color: CHIP_DARK_TEXT };

  for (let t = 0.02; t <= 1; t += 0.02) {
    const darker = bg.map(v => Math.round(v * (1 - t))) as Rgb;
    if (ratio(darker, WHITE) >= TARGET) return { background: toHex(darker), color: '#fff' };
  }
  return { background: '#000000', color: '#fff' };
}

function luminance([r, g, b]: Rgb): number {
  const channel = (v: number): number => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

function parseHex(value: string): null | Rgb {
  const m = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(value.trim());
  if (!m) return null;
  const hex = m[1].length === 3 ? [...m[1]].map(c => c + c).join('') : m[1];
  return [0, 2, 4].map(i => parseInt(hex.slice(i, i + 2), 16)) as Rgb;
}

function ratio(a: Rgb, b: Rgb): number {
  const la = luminance(a);
  const lb = luminance(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

function toHex(c: Rgb): string {
  return '#' + c.map(v => v.toString(16).padStart(2, '0')).join('');
}
