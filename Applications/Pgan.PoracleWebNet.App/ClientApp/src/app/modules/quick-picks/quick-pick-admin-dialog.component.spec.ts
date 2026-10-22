import * as fs from 'fs';
import * as path from 'path';

import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { QUEST_REWARD_POKEMON, QUICK_PICK_ICONS, QuickPickAdminDialogComponent } from './quick-pick-admin-dialog.component';
import { QuickPickDefinition } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfigService } from '../../core/services/config.service';
import { QuickPickService } from '../../core/services/quick-pick.service';

describe('QuickPickAdminDialogComponent', () => {
  let component: QuickPickAdminDialogComponent;
  let quickPickService: { saveAdmin: jest.Mock; saveUser: jest.Mock };

  function setup(data: QuickPickDefinition | null, isAdmin = true): void {
    quickPickService = {
      saveAdmin: jest.fn(() => of({})),
      saveUser: jest.fn(() => of({})),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close: jest.fn() } },
        { provide: QuickPickService, useValue: quickPickService },
        { provide: AuthService, useValue: { isAdmin: () => isAdmin } },
      ],
      imports: [QuickPickAdminDialogComponent],
    });

    const fixture = TestBed.createComponent(QuickPickAdminDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    return fixture;
  }

  function savedDefinition(): QuickPickDefinition {
    const call = quickPickService.saveAdmin.mock.calls[0] ?? quickPickService.saveUser.mock.calls[0];
    return call[0] as QuickPickDefinition;
  }

  describe('editing an existing definition', () => {
    // The built-in "nundo" preset is { minIv: 0, maxIv: 0 } -- that is the entire point of it. Skipping
    // falsy values on save turned "0% IV only" into "any IV" for everyone who applied it afterwards.
    // See #654.
    const nundo: QuickPickDefinition = {
      id: 'nundo',
      name: 'Nundo',
      alarmType: 'monster',
      category: 'PvP',
      description: 'Zero IV',
      enabled: true,
      filters: { maxIv: 0, minIv: 0 },
      icon: 'pokeball',
      scope: 'global',
      sortOrder: 1,
    };

    it('keeps a stored filter whose value is zero', () => {
      setup(nundo);

      component.save();

      const saved = savedDefinition();
      expect(saved.filters['minIv']).toBe(0);
      expect(saved.filters['maxIv']).toBe(0);
    });

    it('keeps a stored key the dialog has no control for', () => {
      // quick-pick-apply reads filters['clean'] as its base bitmask, and no form exposes it.
      setup({ ...nundo, filters: { clean: 5, maxIv: 0, minIv: 0 } });

      component.save();

      expect(savedDefinition().filters['clean']).toBe(5);
    });
  });

  describe('changing the alarm type', () => {
    const monsterPick: QuickPickDefinition = {
      id: 'p',
      name: 'P',
      alarmType: 'monster',
      category: 'PvP',
      description: '',
      enabled: true,
      filters: { clean: 5, minIv: 0, ping: '<@&123>', pvpRankingLeague: 1500, template: 'custom' },
      icon: 'pokeball',
      scope: 'global',
      sortOrder: 1,
    };

    it('drops the previous type’s filters', () => {
      setup(monsterPick);
      component.mainForm.patchValue({ alarmType: 'lure' });

      component.save();

      expect(savedDefinition().filters['pvpRankingLeague']).toBeUndefined();
      expect(savedDefinition().filters['minIv']).toBeUndefined();
    });

    it('keeps the keys that belong to no type', () => {
      // All four are properties of every alarm model and are exposed by no form. Clearing wholesale
      // reset a pick's auto-delete bits (#671) and dropped its ping target and template (#674).
      setup(monsterPick);
      component.mainForm.patchValue({ alarmType: 'lure' });

      component.save();

      const filters = savedDefinition().filters;
      expect(filters['clean']).toBe(5);
      expect(filters['ping']).toBe('<@&123>');
      expect(filters['template']).toBe('custom');
    });
  });

  describe('creating a new definition', () => {
    it('does not write out the form defaults that mean "not set"', () => {
      // Most numeric controls default to 0. Persisting them would put an explicit minIv, pokemonId and
      // pvpRankingLeague of 0 on every new pick, which is the mirror image of the bug above.
      setup(null);
      component.mainForm.patchValue({ name: 'Fresh', alarmType: 'monster' });

      component.save();

      const filters = savedDefinition().filters;
      expect(filters['minIv']).toBeUndefined();
      expect(filters['pokemonId']).toBeUndefined();
      expect(filters['pvpRankingLeague']).toBeUndefined();
    });

    it('still writes a non-default value', () => {
      setup(null);
      component.mainForm.patchValue({ name: 'Hundo', alarmType: 'monster' });
      component.monsterForm.patchValue({ minIv: 100 });

      component.save();

      expect(savedDefinition().filters['minIv']).toBe(100);
    });
  });

  describe('scope', () => {
    it('keeps a user-scoped pick user-scoped when an admin edits it', () => {
      // Deciding from isAdmin alone republished an admin's own personal pick to every user. See #631.
      setup({
        id: 'mine',
        name: 'Mine',
        alarmType: 'monster',
        category: 'Common',
        description: '',
        enabled: true,
        filters: {},
        icon: 'bolt',
        scope: 'user',
        sortOrder: 0,
      });

      component.save();

      expect(quickPickService.saveUser).toHaveBeenCalled();
      expect(quickPickService.saveAdmin).not.toHaveBeenCalled();
    });

    it('publishes a new pick globally when an admin creates it', () => {
      setup(null);
      component.mainForm.patchValue({ name: 'Global', alarmType: 'monster' });

      component.save();

      expect(quickPickService.saveAdmin).toHaveBeenCalled();
    });
  });

  describe('built in the product vocabulary', () => {
    const pick = (over: Partial<QuickPickDefinition>): QuickPickDefinition => ({
      id: 'p',
      name: 'P',
      alarmType: 'monster',
      category: 'Common',
      description: '',
      enabled: true,
      filters: {},
      icon: 'bolt',
      scope: 'global',
      sortOrder: 0,
      ...over,
    });

    it('offers icons to pick rather than a box to type a ligature into', () => {
      const fixture = setup(null);
      const choices = (fixture.nativeElement as HTMLElement).querySelectorAll('[data-test="icon-choice"]');

      expect(choices.length).toBe(QUICK_PICK_ICONS.length);
      (choices[1] as HTMLButtonElement).click();
      expect(component.mainForm.controls.icon.value).toBe(QUICK_PICK_ICONS[1]);
    });

    it('keeps a stored icon that is not in the list, offered first and selected', () => {
      const fixture = setup(pick({ icon: 'pokeball' }));
      const first = (fixture.nativeElement as HTMLElement).querySelector('[data-test="icon-choice"]')!;

      expect(component.iconChoices()[0]).toBe('pokeball');
      expect(first.getAttribute('aria-checked')).toBe('true');
      component.save();
      expect(savedDefinition().icon).toBe('pokeball');
    });

    it('stores the species picked in the Pokémon selector', () => {
      setup(null);
      component.mainForm.patchValue({ name: 'Pikachu', alarmType: 'monster' });

      component.onPokemonPicked([25]);
      component.save();

      expect(savedDefinition().filters['pokemonId']).toBe(25);
    });

    it('treats the selector’s All Pokémon as any, which a max battle stores as 9000', () => {
      setup(pick({ alarmType: 'maxbattle', filters: { pokemonId: 150 } }));
      expect(component.pokemonInitialIds()).toEqual([150]);

      component.onPokemonPicked([0]);
      expect(component.maxBattleForm.controls.pokemonId.value).toBe(9000);
      expect(component.pokemonInitialIds()).toEqual([]);
    });

    it('resets a stored species back to any Pokémon', () => {
      setup(pick({ filters: { pokemonId: 25 } }));

      component.resetPokemon();
      component.save();

      expect(savedDefinition().filters['pokemonId']).toBe(0);
    });

    it('stores a switch as the 0/1 the filter has always held', () => {
      setup(pick({ alarmType: 'raid', filters: {} }));

      component.setOn(component.raidForm, 'exclusive', true);
      component.save();
      expect(savedDefinition().filters['exclusive']).toBe(1);
      expect(component.isOn(component.raidForm, 'exclusive')).toBe(true);
    });

    it('shows the PvP rank fields only once a league is chosen', () => {
      const fixture = setup(null);
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('[data-test="pvp-ranks"]')).toBeNull();

      component.monsterForm.patchValue({ pvpRankingLeague: 1500 });
      fixture.detectChanges();
      expect(el.querySelector('[data-test="pvp-ranks"]')).toBeTruthy();
    });
  });

  /**
   * The editor shows a control only for a field applying the pick reads. Applying deserialises the
   * filters into the type's model (Monster through QuickPickService.SafeMonsterFilterKeys, plus the
   * pokemonId ApplyMonsterAsync reads itself), so a control for anything else is a setting that does
   * nothing. The quest editor offered a species picker the Quest model has no field for.
   */
  describe('shows only the controls the apply path reads', () => {
    const repo = path.join(__dirname, '../../../../../../..');
    const camel = (name: string) => name.charAt(0).toLowerCase() + name.slice(1);

    function modelFields(model: string): Set<string> {
      const source = fs.readFileSync(path.join(repo, `Core/Pgan.PoracleWebNet.Core.Models/${model}.cs`), 'utf8');
      return new Set([...source.matchAll(/public\s+[\w?<>]+\s+(\w+)\s*\{\s*get/g)].map(m => camel(m[1])));
    }

    function monsterFields(): Set<string> {
      const source = fs.readFileSync(path.join(repo, 'Core/Pgan.PoracleWebNet.Core.Services/QuickPickService.cs'), 'utf8');
      const block = /SafeMonsterFilterKeys = new\([^)]*\)\s*\{([^}]*)\}/.exec(source)![1];
      return new Set([...[...block.matchAll(/"(\w+)"/g)].map(m => m[1]), 'pokemonId']);
    }

    const MODELS: Record<string, string> = {
      raid: 'Raid',
      egg: 'Egg',
      gym: 'Gym',
      invasion: 'Invasion',
      lure: 'Lure',
      maxbattle: 'MaxBattle',
      nest: 'Nest',
      quest: 'Quest',
    };

    const EXPECTED: Record<string, string[]> = {
      raid: ['exclusive', 'form', 'level', 'pokemonId', 'team'],
      egg: ['exclusive', 'level', 'team'],
      gym: ['battleChanges', 'slotChanges', 'team'],
      invasion: ['gender', 'gruntType'],
      lure: ['lureId'],
      maxbattle: ['evolution', 'form', 'gmax', 'level', 'move', 'pokemonId'],
      monster: [
        'form',
        'gender',
        'maxCp',
        'maxIv',
        'maxLevel',
        'maxSize',
        'maxWeight',
        'minCp',
        'minIv',
        'minLevel',
        'minWeight',
        'pokemonId',
        'pvpRankingBest',
        'pvpRankingLeague',
        'pvpRankingMinCp',
        'pvpRankingWorst',
        'size',
      ],
      nest: ['minSpawnAvg', 'pokemonId'],
      quest: ['reward', 'rewardType', 'shiny'],
    };

    function visibleControls(type: string, filters: Record<string, unknown> = {}): string[] {
      const fixture = setup({
        id: 'x',
        name: 'X',
        alarmType: type,
        category: 'Common',
        description: '',
        enabled: true,
        filters,
        icon: 'bolt',
        scope: 'global',
        sortOrder: 0,
      });
      fixture.detectChanges();
      const section = (fixture.nativeElement as HTMLElement).querySelector('.filter-section')!;
      const names = [
        ...Array.from(section.querySelectorAll('[formControlName]')).map(el => el.getAttribute('formControlName')!),
        ...Array.from(section.querySelectorAll('[data-control]')).map(el => el.getAttribute('data-control')!),
      ];
      return [...new Set(names)].sort();
    }

    it.each(Object.keys(EXPECTED))('%s', type => {
      // A league is set so the PvP rank fields, shown only with one, are counted.
      const shown = visibleControls(type, type === 'monster' ? { pvpRankingLeague: 1500 } : {});
      expect(shown).toEqual(EXPECTED[type]);

      const applied = type === 'monster' ? monsterFields() : modelFields(MODELS[type]);
      expect(shown.filter(name => !applied.has(name))).toEqual([]);
    });

    it('offers the Pokémon picker on an encounter quest, writing the reward, and no reward box beside it', () => {
      const shown = visibleControls('quest', { reward: 147, rewardType: QUEST_REWARD_POKEMON });

      expect(shown).toEqual(['reward', 'rewardType', 'shiny']);
      expect(component.pokemonControl()).toBe(component.questForm.controls.reward);
      expect(component.pokemonInitialIds()).toEqual([147]);
    });

    it('keeps a stored quest pokemonId it no longer shows', () => {
      setup({
        id: 'q',
        name: 'Q',
        alarmType: 'quest',
        category: 'Quests',
        description: '',
        enabled: true,
        filters: { pokemonId: 25, rewardType: 3 },
        icon: 'bolt',
        scope: 'global',
        sortOrder: 0,
      });

      component.save();

      expect(savedDefinition().filters['pokemonId']).toBe(25);
      expect(savedDefinition().filters['rewardType']).toBe(3);
    });

    const encounterPick = (reward: number, rewardType: number): QuickPickDefinition => ({
      id: 'dratini',
      name: 'Dratini encounter',
      alarmType: 'quest',
      category: 'Quests',
      description: '',
      enabled: true,
      filters: { reward, rewardType },
      icon: 'bolt',
      scope: 'global',
      sortOrder: 0,
    });

    it('opens a stored encounter quest with its species, and saves it unchanged', () => {
      setup(encounterPick(147, QUEST_REWARD_POKEMON));

      expect(component.questForm.controls.reward.value).toBe(147);
      component.save();

      expect(savedDefinition().filters['reward']).toBe(147);
      expect(savedDefinition().filters['rewardType']).toBe(QUEST_REWARD_POKEMON);
    });

    it('clears the reward when the reward type moves off an encounter, so a species id is not saved as an item id', () => {
      setup(encounterPick(147, QUEST_REWARD_POKEMON));

      component.questForm.controls.rewardType.setValue(2);
      component.save();

      expect(savedDefinition().filters['reward']).toBe(0);
      expect(savedDefinition().filters['rewardType']).toBe(2);
    });

    it('clears an item reward when the reward type moves onto an encounter, so the item id is not a species', () => {
      setup(encounterPick(1301, 2));

      component.questForm.controls.rewardType.setValue(QUEST_REWARD_POKEMON);

      expect(component.questForm.controls.reward.value).toBe(0);
      expect(component.pokemonInitialIds()).toEqual([]);
    });

    it('rebuilds the Pokémon picker when the type changes', () => {
      setup(null);
      const before = component.selectorKey();

      component.mainForm.patchValue({ alarmType: 'raid' });

      expect(component.selectorKey()).toBeGreaterThan(before);
    });
  });
});
