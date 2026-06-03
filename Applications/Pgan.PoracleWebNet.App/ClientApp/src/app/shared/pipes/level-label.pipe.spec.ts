import { TestBed } from '@angular/core/testing';

import { LevelLabelPipe } from './level-label.pipe';
import { I18nService } from '../../core/services/i18n.service';

/**
 * Mirror of ngx-translate's "key not found" behavior: if a key has no entry,
 * the translated string equals the key. Tests configure `knownKeys` to control
 * the boundary between "translated" and "missing".
 */
class FakeI18n {
  knownKeys = new Set<string>();
  instant(key: string): string {
    return this.knownKeys.has(key) ? `TR(${key})` : key;
  }
}

describe('LevelLabelPipe', () => {
  let pipe: LevelLabelPipe;
  let i18n: FakeI18n;

  beforeEach(() => {
    TestBed.resetTestingModule();
    i18n = new FakeI18n();
    // Seed with every key the canonical 19 levels rely on, plus ANY + CUSTOM.
    for (let v = 1; v <= 19; v++) i18n.knownKeys.add(`RAIDS.LEVEL.RAID_${v}`);
    i18n.knownKeys.add('RAIDS.LEVEL.ANY');
    i18n.knownKeys.add('RAIDS.LEVEL.CUSTOM');
    TestBed.configureTestingModule({
      providers: [{ provide: I18nService, useValue: i18n }, LevelLabelPipe],
    });
    pipe = TestBed.inject(LevelLabelPipe);
  });

  it('translates every known level via its RAID_N key', () => {
    for (let v = 1; v <= 19; v++) {
      expect(pipe.transform(v)).toBe(`TR(RAIDS.LEVEL.RAID_${v})`);
    }
  });

  it('translates 9000 as ANY', () => {
    expect(pipe.transform(9000)).toBe('TR(RAIDS.LEVEL.ANY)');
  });

  it('formats custom levels with the CUSTOM key prefix + integer', () => {
    expect(pipe.transform(42)).toBe('TR(RAIDS.LEVEL.CUSTOM) 42');
    expect(pipe.transform(20)).toBe('TR(RAIDS.LEVEL.CUSTOM) 20');
  });

  it('falls back to the CUSTOM label when a known-level key is missing from i18n', () => {
    // Simulate the future case where the canonical list grows to 20 but the
    // locale file hasn't been updated yet — the model would surface RAID_20
    // as a labelKey but the i18n returns the bare key.
    i18n.knownKeys.delete('RAIDS.LEVEL.RAID_7');
    expect(pipe.transform(7)).toBe('TR(RAIDS.LEVEL.CUSTOM) 7');
  });
});
