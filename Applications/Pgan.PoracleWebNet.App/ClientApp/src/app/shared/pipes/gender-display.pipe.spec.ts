import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { GenderDisplayPipe } from './gender-display.pipe';

describe('GenderDisplayPipe', () => {
  let pipe: GenderDisplayPipe;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), GenderDisplayPipe],
    });
    pipe = TestBed.inject(GenderDisplayPipe);
  });

  it('should return male symbol for gender 1', () => {
    expect(pipe.transform(1)).toBe('\u2642 COMMON.MALE');
  });

  it('should return female symbol for gender 2', () => {
    expect(pipe.transform(2)).toBe('\u2640 COMMON.FEMALE');
  });

  it('should return "All" key for gender 0', () => {
    expect(pipe.transform(0)).toBe('COMMON.ALL');
  });

  it('should return "All" key for any unknown gender value', () => {
    expect(pipe.transform(3)).toBe('COMMON.ALL');
    expect(pipe.transform(-1)).toBe('COMMON.ALL');
    expect(pipe.transform(99)).toBe('COMMON.ALL');
  });
});
