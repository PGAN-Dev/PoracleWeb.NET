import { LeagueNamePipe } from './league-name.pipe';

describe('LeagueNamePipe', () => {
  const pipe = new LeagueNamePipe();

  it('should return "Little" for 500', () => {
    expect(pipe.transform(500)).toBe('Little');
  });

  it('should return "Great" for 1500', () => {
    expect(pipe.transform(1500)).toBe('Great');
  });

  it('should return "Ultra" for 2500', () => {
    expect(pipe.transform(2500)).toBe('Ultra');
  });

  it('should return the number as string for unknown leagues', () => {
    expect(pipe.transform(0)).toBe('0');
    expect(pipe.transform(9999)).toBe('9999');
  });
});
