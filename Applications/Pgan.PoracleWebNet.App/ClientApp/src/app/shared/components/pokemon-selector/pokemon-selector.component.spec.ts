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

  function setup(ivFilters: boolean, opts: { initialIds?: number[]; multi?: boolean } = {}) {
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
    fixture.componentRef.setInput('multi', opts.multi ?? true);
    fixture.componentRef.setInput('ivFilters', ivFilters);
    if (opts.initialIds) fixture.componentRef.setInput('initialIds', opts.initialIds);
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

  it('says nothing in pickers whose alerts have no IV filters (raids, nests, quests)', () => {
    setup(false);
    pick(MINIOR);

    expect(note()).toBeNull();
  });

  describe('initialIds', () => {
    it('shows a stored single pick as chosen, without emitting it', () => {
      setup(false, { initialIds: [52], multi: false });
      const emitted = jest.fn();
      fixture.componentInstance.selectionChange.subscribe(emitted);

      expect(fixture.componentInstance.selectedPokemon()).toEqual([MEOWTH]);
      expect(fixture.componentInstance.searchControl.value).toContain('Meowth');
      expect(emitted).not.toHaveBeenCalled();
    });

    it('leaves the selector empty when no ids are given, as every existing caller does', () => {
      setup(false);
      expect(fixture.componentInstance.selectedPokemon()).toEqual([]);
    });
  });
});
