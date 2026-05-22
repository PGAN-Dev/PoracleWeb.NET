import { TestBed } from '@angular/core/testing';

import { LevelLabelPipe } from './level-label.pipe';
import { I18nService } from '../../core/services/i18n.service';

class FakeI18n {
  instant(key: string): string {
    return key;
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

  it('formats every known level as its RAID_N key', () => {
    for (let v = 1; v <= 19; v++) {
      expect(pipe.transform(v)).toBe(`RAIDS.LEVEL.RAID_${v}`);
    }
  });

  it('formats 9000 as ANY', () => {
    expect(pipe.transform(9000)).toBe('RAIDS.LEVEL.ANY');
  });

  it('formats custom levels with the integer suffix', () => {
    expect(pipe.transform(42)).toBe('RAIDS.LEVEL.CUSTOM 42');
    expect(pipe.transform(20)).toBe('RAIDS.LEVEL.CUSTOM 20');
  });
});
