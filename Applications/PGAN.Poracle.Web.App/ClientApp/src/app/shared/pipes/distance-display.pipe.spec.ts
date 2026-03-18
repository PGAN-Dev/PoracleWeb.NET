import { DistanceDisplayPipe } from './distance-display.pipe';

describe('DistanceDisplayPipe', () => {
  const pipe = new DistanceDisplayPipe();

  it('should return "Using Areas" for distance 0', () => {
    expect(pipe.transform(0)).toBe('Using Areas');
  });

  it('should return meters for distances under 1000', () => {
    expect(pipe.transform(500)).toBe('500 m');
    expect(pipe.transform(1)).toBe('1 m');
    expect(pipe.transform(999)).toBe('999 m');
  });

  it('should return whole km for exact kilometer values', () => {
    expect(pipe.transform(1000)).toBe('1 km');
    expect(pipe.transform(5000)).toBe('5 km');
    expect(pipe.transform(10000)).toBe('10 km');
  });

  it('should return km with one decimal for fractional values', () => {
    expect(pipe.transform(1500)).toBe('1.5 km');
    expect(pipe.transform(2300)).toBe('2.3 km');
  });

  it('should handle large distances', () => {
    expect(pipe.transform(100000)).toBe('100 km');
  });
});
