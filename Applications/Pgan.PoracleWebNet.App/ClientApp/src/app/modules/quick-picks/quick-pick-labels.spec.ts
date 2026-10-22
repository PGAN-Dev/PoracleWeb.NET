import * as fs from 'fs';
import * as path from 'path';

import { BUILT_IN_QUICK_PICKS, quickPickAlarmTypeLabel, quickPickDescription, quickPickName } from './quick-pick-labels';
import { QuickPickDefinition } from '../../core/models';

const german: Record<string, string> = {
  'QUICK_PICKS.ALARM_TYPES.LURE': 'Lockmodul',
  'QUICK_PICKS.ALARM_TYPES.MONSTER': 'Pokemon',
  'QUICK_PICKS.BUILT_IN.HUNDO.DESCRIPTION': 'Alle wilden Pokemon mit perfekten IVs',
  'QUICK_PICKS.BUILT_IN.HUNDO.NAME': '100 % IV Pokemon',
};
const de = { instant: (key: string) => german[key] ?? key };

const pick = (overrides: Partial<QuickPickDefinition>): QuickPickDefinition => ({
  id: 'hundo',
  name: '100% IV Pokemon',
  alarmType: 'monster',
  category: 'Common',
  description: 'Track all perfect IV wild spawns',
  enabled: true,
  filters: {},
  icon: 'star',
  scope: 'global',
  sortOrder: 1,
  ...overrides,
});

describe('quick pick labels', () => {
  it('translates a built-in whose stored text is still the seed', () => {
    expect(quickPickName(pick({}), de)).toBe('100 % IV Pokemon');
    expect(quickPickDescription(pick({}), de)).toBe('Alle wilden Pokemon mit perfekten IVs');
  });

  it('shows an admin-edited built-in as typed', () => {
    expect(quickPickName(pick({ name: 'Hundos only' }), de)).toBe('Hundos only');
    expect(quickPickDescription(pick({ description: 'Our server favourite' }), de)).toBe('Our server favourite');
  });

  it('never translates a personal pick, even one that reuses a built-in id', () => {
    expect(quickPickName(pick({ scope: 'user' }), de)).toBe('100% IV Pokemon');
  });

  it('keeps the stored text when the locale has no entry', () => {
    expect(quickPickName(pick({ id: 'nundo', name: '0% IV Pokemon', description: 'Track all zero IV wild spawns' }), de)).toBe(
      '0% IV Pokemon',
    );
  });

  it('names alarm types instead of showing the raw value', () => {
    expect(quickPickAlarmTypeLabel('monster', de)).toBe('Pokemon');
    expect(quickPickAlarmTypeLabel('lure', de)).toBe('Lockmodul');
    expect(quickPickAlarmTypeLabel('somethingnew', de)).toBe('somethingnew');
  });

  /** The table must be the seed, byte for byte, or a seeded pick would never match and stay English. */
  it('matches the seed in QuickPickService.cs', () => {
    const source = fs.readFileSync(
      path.join(__dirname, '../../../../../../../Core/Pgan.PoracleWebNet.Core.Services/QuickPickService.cs'),
      'utf8',
    );
    const seeded = [...source.matchAll(/new\(\) \{ Id = "([^"]+)", Name = "([^"]+)", Description = "([^"]+)"/g)].map(m => ({
      id: m[1],
      name: m[2],
      description: m[3],
    }));
    expect(seeded.length).toBeGreaterThan(20);
    expect(Object.fromEntries(seeded.map(s => [s.id, { name: s.name, description: s.description }]))).toEqual(BUILT_IN_QUICK_PICKS);
  });

  it('has a translation for every built-in in every locale', () => {
    const dir = path.join(__dirname, '../../../assets/i18n');
    for (const file of fs.readdirSync(dir).filter(f => f.endsWith('.json'))) {
      const builtIn = JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8')).QUICK_PICKS.BUILT_IN;
      for (const id of Object.keys(BUILT_IN_QUICK_PICKS)) {
        const entry = builtIn[id.toUpperCase().replace(/-/g, '_')];
        expect({ id, file, ok: Boolean(entry?.NAME && entry?.DESCRIPTION) }).toEqual({ id, file, ok: true });
      }
    }
  });
});
