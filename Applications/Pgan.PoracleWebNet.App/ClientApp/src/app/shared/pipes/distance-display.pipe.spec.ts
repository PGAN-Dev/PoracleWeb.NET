import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { DistanceDisplayPipe } from './distance-display.pipe';

describe('DistanceDisplayPipe', () => {
  let pipe: DistanceDisplayPipe;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), DistanceDisplayPipe],
    });
    pipe = TestBed.inject(DistanceDisplayPipe);
  });

  it('should return translated "Using Areas" for distance 0', () => {
    expect(pipe.transform(0)).toBe('COMMON.USING_AREAS');
  });

  it('should return translated meters for distances under 1000', () => {
    expect(pipe.transform(500)).toBe('COMMON.DISTANCE_M');
    expect(pipe.transform(1)).toBe('COMMON.DISTANCE_M');
    expect(pipe.transform(999)).toBe('COMMON.DISTANCE_M');
  });

  it('should return translated km for exact kilometer values', () => {
    expect(pipe.transform(1000)).toBe('COMMON.DISTANCE_KM');
    expect(pipe.transform(5000)).toBe('COMMON.DISTANCE_KM');
    expect(pipe.transform(10000)).toBe('COMMON.DISTANCE_KM');
  });

  it('should return translated km for fractional values', () => {
    expect(pipe.transform(1500)).toBe('COMMON.DISTANCE_KM');
    expect(pipe.transform(2300)).toBe('COMMON.DISTANCE_KM');
  });

  it('should handle large distances', () => {
    expect(pipe.transform(100000)).toBe('COMMON.DISTANCE_KM');
  });
});
