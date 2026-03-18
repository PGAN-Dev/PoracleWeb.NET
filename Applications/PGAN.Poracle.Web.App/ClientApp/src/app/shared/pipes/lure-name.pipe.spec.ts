import { LureNamePipe } from './lure-name.pipe';

describe('LureNamePipe', () => {
  const pipe = new LureNamePipe();

  it('should return "Normal" for 501', () => {
    expect(pipe.transform(501)).toBe('Normal');
  });

  it('should return "Glacial" for 502', () => {
    expect(pipe.transform(502)).toBe('Glacial');
  });

  it('should return "Mossy" for 503', () => {
    expect(pipe.transform(503)).toBe('Mossy');
  });

  it('should return "Magnetic" for 504', () => {
    expect(pipe.transform(504)).toBe('Magnetic');
  });

  it('should return "Rainy" for 505', () => {
    expect(pipe.transform(505)).toBe('Rainy');
  });

  it('should return "Golden" for 506', () => {
    expect(pipe.transform(506)).toBe('Golden');
  });

  it('should return fallback for unknown lure IDs', () => {
    expect(pipe.transform(0)).toBe('Lure #0');
    expect(pipe.transform(999)).toBe('Lure #999');
  });
});
