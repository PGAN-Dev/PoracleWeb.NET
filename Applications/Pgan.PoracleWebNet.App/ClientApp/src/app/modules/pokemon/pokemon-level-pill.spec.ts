import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';

import { PokemonListComponent } from './pokemon-list.component';

/**
 * The card's level pill appeared only for a max level under 35, from when 35 was the top. The no-filter
 * value is 55, so a rule capped anywhere from 35 to 54 filtered Pokemon out without the card saying so.
 */
describe('PokemonListComponent level pill', () => {
  let component: PokemonListComponent;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTranslateService(),
        { provide: MatDialog, useValue: { open: jest.fn() } },
      ],
    });
    component = TestBed.createComponent(PokemonListComponent).componentInstance;
  });

  it.each([
    [0, 35],
    [0, 40],
    [0, 50],
    [0, 54],
  ])('shows L%i-%i, which excludes the top levels', (minLevel, maxLevel) => {
    expect(component.hasLevelFilter({ maxLevel, minLevel })).toBe(true);
  });

  it('shows a raised minimum and a low maximum', () => {
    expect(component.hasLevelFilter({ maxLevel: 55, minLevel: 20 })).toBe(true);
    expect(component.hasLevelFilter({ maxLevel: 30, minLevel: 0 })).toBe(true);
  });

  it('hides the no-filter range', () => {
    expect(component.hasLevelFilter({ maxLevel: 55, minLevel: 0 })).toBe(false);
  });
});
