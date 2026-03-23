import { GenderDisplayPipe } from './gender-display.pipe';

describe('GenderDisplayPipe', () => {
  const pipe = new GenderDisplayPipe();

  it('should return male symbol for gender 1', () => {
    expect(pipe.transform(1)).toBe('\u2642 Male');
  });

  it('should return female symbol for gender 2', () => {
    expect(pipe.transform(2)).toBe('\u2640 Female');
  });

  it('should return "All" for gender 0', () => {
    expect(pipe.transform(0)).toBe('All');
  });

  it('should return "All" for any unknown gender value', () => {
    expect(pipe.transform(3)).toBe('All');
    expect(pipe.transform(-1)).toBe('All');
    expect(pipe.transform(99)).toBe('All');
  });
});
