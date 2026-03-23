import { TeamNamePipe } from './team-name.pipe';

describe('TeamNamePipe', () => {
  const pipe = new TeamNamePipe();

  it('should return "Neutral" for team 0', () => {
    expect(pipe.transform(0)).toBe('Neutral');
  });

  it('should return "Mystic" for team 1', () => {
    expect(pipe.transform(1)).toBe('Mystic');
  });

  it('should return "Valor" for team 2', () => {
    expect(pipe.transform(2)).toBe('Valor');
  });

  it('should return "Instinct" for team 3', () => {
    expect(pipe.transform(3)).toBe('Instinct');
  });

  it('should return fallback for unknown team values', () => {
    expect(pipe.transform(4)).toBe('Team 4');
    expect(pipe.transform(-1)).toBe('Team -1');
  });
});
