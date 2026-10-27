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

  function setup(
    ivFilters: boolean,
    opts: { allowedIds?: number[]; initialIds?: number[]; multi?: boolean; pokemon?: PokemonEntry[]; types?: string[] } = {},
  ) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: I18nService, useValue: { instant: (k: string) => k, plural: (k: string) => k } },
        { provide: IconService, useValue: { getPokemonUrl: () => '', getTypeUrl: () => '' } },
        {
          provide: MasterDataService,
          useValue: {
            getAllPokemon$: () => of(opts.pokemon ?? [MEOWTH, MINIOR]),
            getAllTypes: () => opts.types ?? [],
            getPokemonTypes: () => ['Water'],
            getTypeLabel: (t: string) => t,
          },
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
    if (opts.allowedIds) fixture.componentRef.setInput('allowedIds', opts.allowedIds);
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
  describe('adding what a filter shows (#1090)', () => {
    const dex = (n: number) => Array.from({ length: n }, (_, i) => ({ id: i + 1, name: `Mon${i + 1}` }) as PokemonEntry);
    const sut = () => fixture.componentInstance;
    const root = () => fixture.nativeElement as HTMLElement;
    const emitted: number[][] = [];

    function showGen1(): void {
      sut().selectionChange.subscribe(ids => emitted.push(ids));
      sut().toggleGen(sut().generations[0]);
      fixture.detectChanges();
    }

    beforeEach(() => (emitted.length = 0));

    it('adds every species a generation shows in one click, and emits once', () => {
      setup(false, { pokemon: dex(300) });
      showGen1();

      sut().selectAllShown();

      expect(sut().selectedPokemon()).toHaveLength(151);
      expect(emitted).toHaveLength(1);
      expect(emitted[0]).toEqual(dex(151).map(p => p.id));
    });

    it('shows every tile a filter matches rather than the first 200, once the grid is opened out', () => {
      setup(false, { pokemon: dex(400) });
      sut().toggleType('Water');
      fixture.detectChanges();
      expect(sut().shownCount()).toBe(400);

      root().querySelector<HTMLButtonElement>('.tile-more')!.click();
      fixture.detectChanges();

      expect(root().querySelectorAll('.pokemon-tile')).toHaveLength(400);
    });

    describe('a grid that folds instead of scrolling inside the scrolling dialog', () => {
      it('shows the first 24 tiles with Show all, and has no scroll box of its own', () => {
        setup(false, { pokemon: dex(300) });
        showGen1();

        expect(root().querySelectorAll('.pokemon-tile')).toHaveLength(24);
        const more = root().querySelector<HTMLButtonElement>('.tile-more')!;
        expect(more.textContent).toContain('POKEMON_SELECTOR.SHOW_ALL_SELECTED');
        expect(more.getAttribute('aria-expanded')).toBe('false');

        more.click();
        fixture.detectChanges();

        expect(root().querySelectorAll('.pokemon-tile')).toHaveLength(151);
        expect(root().querySelector('.tile-more')?.getAttribute('aria-expanded')).toBe('true');
      });

      it('still selects everything shown while folded', () => {
        setup(false, { pokemon: dex(300) });
        showGen1();

        root().querySelector<HTMLInputElement>('.select-all-shown input')!.click();
        fixture.detectChanges();

        expect(sut().selectedPokemon()).toHaveLength(151);
      });

      it('folds again when the filter changes', () => {
        setup(false, { pokemon: dex(300) });
        showGen1();
        root().querySelector<HTMLButtonElement>('.tile-more')!.click();
        fixture.detectChanges();

        sut().toggleGen(sut().generations[1]);
        fixture.detectChanges();

        expect(root().querySelectorAll('.pokemon-tile')).toHaveLength(24);
      });

      it('offers no Show all when everything fits', () => {
        setup(false, { pokemon: dex(20) });
        showGen1();

        expect(root().querySelectorAll('.pokemon-tile')).toHaveLength(20);
        expect(root().querySelector('.tile-more')).toBeNull();
      });
    });

    it('still caps the search dropdown, which is not a grid', () => {
      setup(false, { pokemon: dex(400) });
      sut().searchText.set('mon');

      expect(sut().filteredPokemon().length).toBeLessThanOrEqual(200);
    });

    it('narrows to the search typed over the grid', () => {
      setup(false, { pokemon: dex(300) });
      showGen1();
      sut().searchText.set('mon1');

      sut().selectAllShown();

      // Mon1, Mon10-19, Mon100-151
      expect(sut().selectedPokemon()).toHaveLength(1 + 10 + 52);
    });

    it('keeps what was picked before and only adds the rest', () => {
      setup(false, { pokemon: dex(300) });
      sut().selectedPokemon.set([dex(300)[199]]);
      showGen1();

      sut().selectAllShown();

      expect(sut().selectedPokemon()).toHaveLength(152);
      expect(sut().selectedPokemon()[0].id).toBe(200);
    });

    it('removes only what the filter shows', () => {
      setup(false, { pokemon: dex(300) });
      sut().selectedPokemon.set([dex(300)[199], dex(300)[0], dex(300)[1]]);
      showGen1();

      sut().deselectAllShown();

      expect(
        sut()
          .selectedPokemon()
          .map(p => p.id),
      ).toEqual([200]);
      expect(emitted.at(-1)).toEqual([200]);
    });

    it('offers only the species the picker allows', () => {
      setup(false, { allowedIds: [3, 6, 9], pokemon: dex(300) });
      showGen1();

      sut().selectAllShown();

      expect(
        sut()
          .selectedPokemon()
          .map(p => p.id),
      ).toEqual([3, 6, 9]);
    });

    describe('one select-all control for what the grid shows', () => {
      const box = () => root().querySelector<HTMLInputElement>('.select-all-shown input[type=checkbox]');
      const status = () => root().querySelector('.shown-status')?.textContent?.trim() ?? '';

      it('is a checkbox labelled with how many the grid shows, never with a zero', () => {
        setup(false, { pokemon: dex(300) });
        showGen1();

        expect(box()?.checked).toBe(false);
        expect(box()?.indeterminate).toBe(false);
        expect(sut().shownCount()).toBe(151);
        expect(root().querySelector('.tile-actions')?.textContent).not.toContain('DESELECT');
      });

      it('is mixed when some of the shown species are picked, and says how many', () => {
        setup(false, { pokemon: dex(300) });
        sut().selectedPokemon.set([dex(300)[0], dex(300)[1]]);
        showGen1();

        expect(box()?.indeterminate).toBe(true);
        expect(status()).toContain('POKEMON_SELECTOR.SHOWN_SELECTED');
      });

      it('checks once every shown species is picked, and a second click clears only those', () => {
        setup(false, { pokemon: dex(300) });
        sut().selectedPokemon.set([dex(300)[199]]);
        showGen1();

        box()!.click();
        fixture.detectChanges();
        expect(box()?.checked).toBe(true);
        expect(sut().selectedPokemon()).toHaveLength(152);

        box()!.click();
        fixture.detectChanges();
        expect(box()?.checked).toBe(false);
        expect(
          sut()
            .selectedPokemon()
            .map(p => p.id),
        ).toEqual([200]);
      });

      it('announces the count politely', () => {
        setup(false, { pokemon: dex(300) });
        showGen1();

        expect(root().querySelector('.shown-status')?.getAttribute('aria-live')).toBe('polite');
      });
    });

    it('collapses a long selection to 12 chips with Show all, and Clear all empties it', () => {
      setup(false, { pokemon: dex(300) });
      showGen1();
      sut().selectAllShown();
      fixture.detectChanges();

      expect(root().querySelectorAll('mat-chip')).toHaveLength(12);
      sut().toggleChips();
      fixture.detectChanges();
      expect(root().querySelectorAll('mat-chip')).toHaveLength(151);

      sut().clearSelection();
      fixture.detectChanges();
      expect(sut().selectedPokemon()).toEqual([]);
      expect(emitted.at(-1)).toEqual([]);
    });

    it('pastes numbers within what the picker allows, and says what it left out', () => {
      setup(false, { allowedIds: [3, 6, 9], pokemon: dex(300) });
      sut().toggleBulkMode();
      sut().bulkControl.setValue('1-10');
      sut().runBulkMatch();

      expect(
        sut()
          .selectedPokemon()
          .map(p => p.id),
      ).toEqual([3, 6, 9]);

      sut().bulkControl.setValue('4');
      sut().runBulkMatch();
      fixture.detectChanges();
      expect(root().querySelector('.bulk-results')?.textContent).toContain('POKEMON_SELECTOR.BULK_NOT_ALLOWED');
    });

    describe('finding a picked species again from the search box', () => {
      it('lists a picked species in the dropdown, marked as picked', () => {
        setup(false, { pokemon: dex(300) });
        sut().selectedPokemon.set([dex(300)[24]]);
        sut().searchText.set('mon25');

        expect(
          sut()
            .filteredPokemon()
            .map(p => p.id),
        ).toContain(25);
      });

      it('removes a picked species when it is chosen again, so one of a thousand can be dropped', () => {
        setup(false, { pokemon: dex(300) });
        sut().selectionChange.subscribe(ids => emitted.push(ids));
        sut().selectedPokemon.set(dex(300));

        sut().onSelected({ option: { value: dex(300)[24] } } as never);

        expect(sut().selectedIds().has(25)).toBe(false);
        expect(sut().selectedPokemon()).toHaveLength(299);
        expect(emitted.at(-1)).toHaveLength(299);
      });

      it('still adds a species that is not picked yet', () => {
        setup(false, { pokemon: dex(300) });

        sut().onSelected({ option: { value: dex(300)[24] } } as never);

        expect(sut().selectedIds().has(25)).toBe(true);
      });
    });
  });

  describe('generation and type chips are one tab stop each', () => {
    const root = () => fixture.nativeElement as HTMLElement;
    const TYPES = ['Bug', 'Fire', 'Water'];

    function open(): void {
      setup(false, { types: TYPES });
      root().querySelector<HTMLButtonElement>('.bulk-pickers-toggle')!.click();
      fixture.detectChanges();
    }

    const group = (cls: string) => root().querySelector<HTMLElement>(`.${cls}[role="toolbar"]`)!;
    const chips = (cls: string) => Array.from(group(cls).querySelectorAll<HTMLButtonElement>('button.gen-chip'));
    const stops = (cls: string) => chips(cls).filter(c => c.tabIndex === 0);
    const key = (el: HTMLElement, k: string) => {
      el.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, cancelable: true, key: k }));
      fixture.detectChanges();
    };

    it('labels each group as a toolbar with a single tab stop', () => {
      open();

      for (const cls of ['gen-group', 'type-group']) {
        expect(group(cls).getAttribute('aria-labelledby')).toBeTruthy();
        expect(stops(cls)).toHaveLength(1);
      }
      expect(stops('gen-group')[0].textContent).toContain('G1');
    });

    it('moves along a group with the arrow keys, wrapping at the ends, and Home and End jump', () => {
      open();
      const gens = chips('gen-group');
      gens[0].focus();

      key(gens[0], 'ArrowRight');
      expect(document.activeElement).toBe(gens[1]);
      expect(stops('gen-group')).toEqual([gens[1]]);

      key(gens[1], 'End');
      expect(document.activeElement).toBe(gens[8]);
      key(gens[8], 'ArrowRight');
      expect(document.activeElement).toBe(gens[0]);
      key(gens[0], 'ArrowLeft');
      expect(document.activeElement).toBe(gens[8]);
      key(gens[8], 'Home');
      expect(document.activeElement).toBe(gens[0]);
    });

    it('leaves Tab and other keys alone', () => {
      open();
      const gens = chips('gen-group');
      gens[0].focus();
      const tab = new KeyboardEvent('keydown', { bubbles: true, cancelable: true, key: 'Tab' });
      gens[0].dispatchEvent(tab);

      expect(tab.defaultPrevented).toBe(false);
      expect(document.activeElement).toBe(gens[0]);
    });

    it('says which chip is on with aria-pressed', () => {
      open();
      const types = chips('type-group');

      types[2].click();
      fixture.detectChanges();

      expect(types.map(c => c.getAttribute('aria-pressed'))).toEqual(['false', 'false', 'true']);
      expect(stops('type-group')).toEqual([types[2]]);
    });
  });
});
