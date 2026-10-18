import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';

import { PokemonListComponent } from './pokemon-list.component';
import { Monster } from '../../core/models';

/**
 * The personal badges say, on the card, that a rule only fires for the trainer's own game accounts.
 * Bits 4 and 8 are reserved, so a rule holding only those has nothing to badge.
 */
describe('PokemonListComponent personal badges', () => {
  let fixture: ComponentFixture<PokemonListComponent>;

  function render(personal: number | undefined): HTMLElement {
    fixture.detectChanges(); // ngOnInit starts a load that the test HTTP backend never answers
    fixture.componentInstance.monsters.set([{ uid: 1, form: 0, maxIv: 100, minIv: 0, personal, pokemonId: 25 } as Monster]);
    fixture.componentInstance.loading.set(false);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

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
    fixture = TestBed.createComponent(PokemonListComponent);
  });

  it('shows the personal shiny badge for bit 1', () => {
    const el = render(1);

    expect(el.querySelector('.personal-shiny-tag')).not.toBeNull();
    expect(el.querySelector('.personal-background-tag')).toBeNull();
  });

  it('shows the background badge for bit 2', () => {
    const el = render(2);

    expect(el.querySelector('.personal-background-tag')).not.toBeNull();
    expect(el.querySelector('.personal-shiny-tag')).toBeNull();
  });

  it('shows both badges for 3', () => {
    const el = render(3);

    expect(el.querySelector('.personal-shiny-tag')).not.toBeNull();
    expect(el.querySelector('.personal-background-tag')).not.toBeNull();
  });

  it('shows no personal badge for bit 4 alone', () => {
    const el = render(4);

    expect(el.querySelector('.personal-shiny-tag')).toBeNull();
    expect(el.querySelector('.personal-background-tag')).toBeNull();
  });

  it('draws a shiny-for-me rule with the shiny sprite, and an ordinary rule without it', () => {
    let el = render(1);
    expect((el.querySelector('img.pokemon-img') as HTMLImageElement).src).toMatch(/\/25_s\.png$/);

    el = render(2);
    expect((el.querySelector('img.pokemon-img') as HTMLImageElement).src).toMatch(/\/25\.png$/);
  });

  it('falls back from a missing shiny sprite to the ordinary one before giving up', () => {
    // An icon pack without shiny art 404s the _s file; the rule must still show its Pokemon.
    const el = render(1);
    const img = el.querySelector('img.pokemon-img') as HTMLImageElement;

    img.dispatchEvent(new Event('error'));
    expect(img.src).toMatch(/\/25\.png$/);
    expect(img.style.display).not.toBe('none');
  });

  it('puts the personal badges with the name, not in the crowded top-right row', () => {
    // Auto-delete, edit and both personal flags in one row ran the last badge off the card.
    const el = render(3);

    expect(el.querySelector('.pokemon-info .personal-shiny-tag')).not.toBeNull();
    expect(el.querySelector('.pokemon-info .personal-background-tag')).not.toBeNull();
    expect(el.querySelector('.card-top-actions .personal-tag')).toBeNull();
  });

  it('shows no personal badge for a rule without the field', () => {
    const el = render(undefined);

    expect(el.querySelector('.alarm-card')).not.toBeNull();
    expect(el.querySelector('.personal-shiny-tag')).toBeNull();
  });
});
