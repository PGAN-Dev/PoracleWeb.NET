import { TestBed } from '@angular/core/testing';

import { LevelLabelPipe } from './level-label.pipe';
import { I18nService } from '../../core/services/i18n.service';

class FakeI18n {
  instant(key: string): string {
    return key; // tests assert on the i18n key, not the translation
  }
}

describe('LevelLabelPipe', () => {
  let pipe: LevelLabelPipe;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [{ provide: I18nService, useClass: FakeI18n }, LevelLabelPipe],
    });
    pipe = TestBed.inject(LevelLabelPipe);
  });

  it('formats standard tiers as T1-T5 keys', () => {
    for (let v = 1; v <= 5; v++) {
      expect(pipe.transform(v)).toBe(`RAIDS.LEVEL.T${v}`);
    }
  });

  it('formats Mega and Elite', () => {
    expect(pipe.transform(6)).toBe('RAIDS.LEVEL.MEGA');
    expect(pipe.transform(7)).toBe('RAIDS.LEVEL.ELITE');
  });

  it('formats 9000 as ANY', () => {
    expect(pipe.transform(9000)).toBe('RAIDS.LEVEL.ANY');
  });

  it('formats custom levels with the numeric badge', () => {
    expect(pipe.transform(42)).toBe('RAIDS.LEVEL.CUSTOM 42');
    expect(pipe.transform(8)).toBe('RAIDS.LEVEL.CUSTOM 8');
  });
});
