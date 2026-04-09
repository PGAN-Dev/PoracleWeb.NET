import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { TeamNamePipe } from './team-name.pipe';

describe('TeamNamePipe', () => {
  let pipe: TeamNamePipe;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), TeamNamePipe],
    });
    pipe = TestBed.inject(TeamNamePipe);
  });

  it('should return translated "Neutral" for team 0', () => {
    expect(pipe.transform(0)).toBe('GYMS.TEAM_NEUTRAL');
  });

  it('should return translated "Mystic" for team 1', () => {
    expect(pipe.transform(1)).toBe('GYMS.TEAM_MYSTIC');
  });

  it('should return translated "Valor" for team 2', () => {
    expect(pipe.transform(2)).toBe('GYMS.TEAM_VALOR');
  });

  it('should return translated "Instinct" for team 3', () => {
    expect(pipe.transform(3)).toBe('GYMS.TEAM_INSTINCT');
  });

  it('should return translated fallback for unknown team values', () => {
    expect(pipe.transform(4)).toBe('GYMS.TEAM_UNKNOWN');
    expect(pipe.transform(-1)).toBe('GYMS.TEAM_UNKNOWN');
  });
});
