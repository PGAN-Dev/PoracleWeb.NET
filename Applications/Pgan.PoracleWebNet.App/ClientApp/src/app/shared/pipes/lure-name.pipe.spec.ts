import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { LureNamePipe } from './lure-name.pipe';

describe('LureNamePipe', () => {
  let pipe: LureNamePipe;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), LureNamePipe],
    });
    pipe = TestBed.inject(LureNamePipe);
  });

  it('should return translated "Normal" for 501', () => {
    expect(pipe.transform(501)).toBe('LURES.TYPE_NORMAL');
  });

  it('should return translated "Glacial" for 502', () => {
    expect(pipe.transform(502)).toBe('LURES.TYPE_GLACIAL');
  });

  it('should return translated "Mossy" for 503', () => {
    expect(pipe.transform(503)).toBe('LURES.TYPE_MOSSY');
  });

  it('should return translated "Magnetic" for 504', () => {
    expect(pipe.transform(504)).toBe('LURES.TYPE_MAGNETIC');
  });

  it('should return translated "Rainy" for 505', () => {
    expect(pipe.transform(505)).toBe('LURES.TYPE_RAINY');
  });

  it('should return translated "Golden" for 506', () => {
    expect(pipe.transform(506)).toBe('LURES.TYPE_GOLDEN');
  });

  it('should return translated fallback for unknown lure IDs', () => {
    expect(pipe.transform(0)).toBe('LURES.TYPE_UNKNOWN');
    expect(pipe.transform(999)).toBe('LURES.TYPE_UNKNOWN');
  });
});
