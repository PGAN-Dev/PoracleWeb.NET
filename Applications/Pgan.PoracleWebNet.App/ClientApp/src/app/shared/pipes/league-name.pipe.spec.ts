import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';

import { LeagueNamePipe } from './league-name.pipe';

describe('LeagueNamePipe', () => {
  let pipe: LeagueNamePipe;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService(), LeagueNamePipe],
    });
    pipe = TestBed.inject(LeagueNamePipe);
  });

  it('should return translated "Little" for 500', () => {
    expect(pipe.transform(500)).toBe('POKEMON.LEAGUE_LITTLE');
  });

  it('should return translated "Great" for 1500', () => {
    expect(pipe.transform(1500)).toBe('POKEMON.LEAGUE_GREAT');
  });

  it('should return translated "Ultra" for 2500', () => {
    expect(pipe.transform(2500)).toBe('POKEMON.LEAGUE_ULTRA');
  });

  it('should return translated "Master" for 10000', () => {
    expect(pipe.transform(10000)).toBe('POKEMON.LEAGUE_MASTER');
  });

  it('should return the number as string for unknown leagues', () => {
    expect(pipe.transform(0)).toBe('0');
    expect(pipe.transform(9999)).toBe('9999');
  });
});
