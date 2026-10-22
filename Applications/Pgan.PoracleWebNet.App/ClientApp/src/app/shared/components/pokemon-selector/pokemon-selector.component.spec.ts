import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { PokemonSelectorComponent } from './pokemon-selector.component';
import { I18nService } from '../../../core/services/i18n.service';
import { IconService } from '../../../core/services/icon.service';
import { MasterDataService, PokemonEntry } from '../../../core/services/masterdata.service';
import { PokemonAvailabilityService } from '../../../core/services/pokemon-availability.service';

describe('PokemonSelectorComponent', () => {
  const MINIOR = { id: 774, name: 'Minior' } as PokemonEntry;
  const MEOWTH = { id: 52, name: 'Meowth' } as PokemonEntry;

  let fixture: ComponentFixture<PokemonSelectorComponent>;

  function setup(ivFilters: boolean) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: I18nService, useValue: { instant: (k: string) => k, plural: (k: string) => k } },
        { provide: IconService, useValue: { getPokemonUrl: () => '', getTypeUrl: () => '' } },
        {
          provide: MasterDataService,
          useValue: { getAllPokemon$: () => of([MEOWTH, MINIOR]), getAllTypes: () => [], getTypeLabel: (t: string) => t },
        },
        {
          provide: PokemonAvailabilityService,
          useValue: { enabled: signal(false), getAvailableCount: () => 0, isAvailable: () => false, load: () => undefined },
        },
      ],
      imports: [PokemonSelectorComponent],
    });

    fixture = TestBed.createComponent(PokemonSelectorComponent);
    fixture.componentRef.setInput('multi', true);
    fixture.componentRef.setInput('ivFilters', ivFilters);
    fixture.detectChanges();
  }

  function pick(...pokemon: PokemonEntry[]) {
    fixture.componentInstance.selectedPokemon.set(pokemon);
    fixture.detectChanges();
  }

  function note(): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector('.minior-note');
  }

  it('says why IV filters will not match the moment Minior is picked for a Pokemon alert', () => {
    setup(true);
    expect(note()).toBeNull();

    pick(MEOWTH, MINIOR);

    expect(note()?.textContent).toContain('POKEMON.MINIOR_IV_NOTE');
  });

  it('drops the note again when Minior is removed', () => {
    setup(true);
    pick(MEOWTH, MINIOR);

    fixture.componentInstance.removePokemon(MINIOR.id);
    fixture.detectChanges();

    expect(note()).toBeNull();
  });

  it('says nothing for other Pokemon', () => {
    setup(true);
    pick(MEOWTH);

    expect(note()).toBeNull();
  });

  describe('search first, bulk pickers behind "Add by type or generation"', () => {
    const root = () => fixture.nativeElement as HTMLElement;
    const toggle = () => root().querySelector<HTMLButtonElement>('.bulk-pickers-toggle');

    it('puts the search field before everything else and hides the generation chips', () => {
      setup(true);

      const first = root().firstElementChild;
      expect(first?.tagName.toLowerCase()).toBe('mat-form-field');
      expect(first?.querySelector('input')).not.toBeNull();
      expect(root().querySelector('.gen-chip')).toBeNull();
      expect(toggle()?.getAttribute('aria-expanded')).toBe('false');
    });

    it('shows the generation chips once the expander is opened', () => {
      setup(true);

      toggle()!.click();
      fixture.detectChanges();

      expect(root().querySelectorAll('.gen-chip').length).toBe(9);
      expect(toggle()?.getAttribute('aria-expanded')).toBe('true');
    });

    it('closing the pickers drops their filter, so the tile grid does not outlive the chips that made it', () => {
      setup(true);
      toggle()!.click();
      fixture.detectChanges();
      root().querySelector<HTMLButtonElement>('.gen-chip')!.click();
      fixture.detectChanges();
      expect(root().querySelector('.tile-grid')).not.toBeNull();

      toggle()!.click();
      fixture.detectChanges();

      expect(fixture.componentInstance.activeGen()).toBeNull();
      expect(root().querySelector('.tile-grid')).toBeNull();
    });

    it('keeps what was picked from the tiles after the pickers close', () => {
      setup(true);
      toggle()!.click();
      fixture.detectChanges();
      root().querySelector<HTMLButtonElement>('.gen-chip')!.click();
      fixture.detectChanges();
      root().querySelector<HTMLButtonElement>('.pokemon-tile')!.click();
      fixture.detectChanges();

      toggle()!.click();
      fixture.detectChanges();

      expect(fixture.componentInstance.selectedIds().has(MEOWTH.id)).toBe(true);
    });
  });

  it('says nothing in pickers whose alerts have no IV filters (raids, nests, quests)', () => {
    setup(false);
    pick(MINIOR);

    expect(note()).toBeNull();
  });
});
