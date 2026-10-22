import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { merge } from 'rxjs';

import { quickPickAlarmTypeLabel } from './quick-pick-labels';
import { QuickPickDefinition } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { QuickPickService } from '../../core/services/quick-pick.service';
import { PokemonSelectorComponent } from '../../shared/components/pokemon-selector/pokemon-selector.component';

/**
 * Filter keys that belong to no particular alarm type, and so survive a type change.
 *
 * All four are properties of every one of the ten alarm models, and none is exposed by any of the
 * per-type forms in `getFilterForm` -- the same test that justified preserving `clean` in #671. The
 * backend's own `QuickPickService.SafeMonsterFilterKeys` lists them alongside it. `ping` in particular
 * is never overridden at apply time, because the apply dialog has no ping control at all.
 *
 * This said `clean` was the only one, which was wrong -- and wrong in the direction that silently drops
 * a user's ping target and template on a type change. See #671, #674.
 */
const TYPE_AGNOSTIC_FILTER_KEYS = new Set(['clean', 'distance', 'ping', 'template']);

/**
 * Icons an admin can give a pick: the built-in picks' own, plus a few that fit other presets. A stored
 * icon outside this list is still offered, first, so editing a pick never changes its icon by itself.
 */
export const QUICK_PICK_ICONS = [
  'bolt',
  'star',
  'stars',
  'star_half',
  'star_border',
  'military_tech',
  'emoji_events',
  'trending_up',
  'catching_pokemon',
  'shield',
  'egg',
  'flash_on',
  'assignment',
  'warning',
  'supervisor_account',
  'place',
  'park',
  'fitness_center',
  'water_drop',
  'ac_unit',
  'eco',
  'diamond',
  'auto_awesome',
  'local_fire_department',
  'open_in_full',
  'close_fullscreen',
  'exposure_zero',
  'favorite',
  'celebration',
  'pets',
];

/** The filter value that means "any Pokémon": 9000 for max battles, as the battle form stores it; 0 elsewhere. */
const ANY_POKEMON: Record<string, number> = { maxbattle: 9000 };

/** Quest reward type for a Pokémon encounter; its `reward` is the species id, as the quest add dialog stores it. */
export const QUEST_REWARD_POKEMON = 7;

/** Stored quick-pick categories are English strings, so the display label comes from this value-to-key map. */
const CATEGORY_KEYS: Record<string, string> = {
  Raids: 'QUICK_PICKS.CATEGORY_RAIDS',
  Common: 'QUICK_PICKS.CATEGORY_COMMON',
  Custom: 'QUICK_PICKS.CATEGORY_CUSTOM',
  Invasions: 'QUICK_PICKS.CATEGORY_INVASIONS',
  'Max Battles': 'QUICK_PICKS.CATEGORY_MAX_BATTLES',
  PvP: 'QUICK_PICKS.CATEGORY_PVP',
  Quests: 'QUICK_PICKS.CATEGORY_QUESTS',
  Size: 'QUICK_PICKS.CATEGORY_SIZE',
};

@Component({
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    PokemonSelectorComponent,
    TranslatePipe,
  ],
  selector: 'app-quick-pick-admin-dialog',
  standalone: true,
  styleUrl: './quick-pick-admin-dialog.component.scss',
  templateUrl: './quick-pick-admin-dialog.component.html',
})
export class QuickPickAdminDialogComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly quickPickService = inject(QuickPickService);
  private readonly snackBar = inject(MatSnackBar);
  readonly alarmTypes = ['monster', 'raid', 'egg', 'quest', 'invasion', 'lure', 'nest', 'gym', 'maxbattle'];
  readonly categories = ['Common', 'PvP', 'Size', 'Raids', 'Quests', 'Invasions', 'Max Battles', 'Custom'];
  readonly data = inject<QuickPickDefinition | null>(MAT_DIALOG_DATA, {
    optional: true,
  });

  readonly dialogRef = inject(MatDialogRef<QuickPickAdminDialogComponent>);
  eggForm = this.fb.group({
    exclusive: [0],
    level: [5],
    team: [0],
  });

  gymForm = this.fb.group({
    battleChanges: [0],
    slotChanges: [0],
    team: [0],
  });

  invasionForm = this.fb.group({
    gender: [0],
    gruntType: [''],
  });

  readonly isAdmin = this.auth.isAdmin();

  readonly isEdit = !!this.data;

  lureForm = this.fb.group({
    lureId: [0],
  });

  mainForm = this.fb.group({
    name: [this.data?.name ?? '', Validators.required],
    alarmType: [this.data?.alarmType ?? 'monster'],
    category: [this.data?.category ?? 'Common'],
    description: [this.data?.description ?? ''],
    enabled: [this.data?.enabled ?? true],
    icon: [this.data?.icon ?? 'bolt'],
    sortOrder: [this.data?.sortOrder ?? 0],
  });

  maxBattleForm = this.fb.group({
    evolution: [9000],
    form: [0],
    gmax: [0],
    level: [9000],
    move: [9000],
    pokemonId: [9000],
  });

  monsterForm = this.fb.group({
    form: [0],
    gender: [0],
    maxCp: [9000],
    maxIv: [100],
    maxLevel: [55],
    maxSize: [5],
    maxWeight: [9000000],
    minCp: [0],
    minIv: [0],
    minLevel: [0],
    minWeight: [0],
    pokemonId: [0],
    pvpRankingBest: [1],
    pvpRankingLeague: [0],
    pvpRankingMinCp: [0],
    pvpRankingWorst: [4096],
    size: [-1],
  });

  nestForm = this.fb.group({
    minSpawnAvg: [0],
    pokemonId: [0],
  });

  // No pokemonId: the Quest model has none, so applying a pick drops it. An encounter quest names its
  // species in `reward` (reward type 7), which is where the Pokémon picker writes for a quest.
  questForm = this.fb.group({
    reward: [0],
    rewardType: [0],
    shiny: [0],
  });

  raidForm = this.fb.group({
    exclusive: [0],
    form: [0],
    level: [5],
    pokemonId: [0],
    team: [0],
  });

  readonly saving = signal(false);

  /** Bumped to rebuild the Pokémon selector, which has no way to clear a single pick from outside. */
  readonly selectorKey = signal(0);

  /**
   * Where this pick belongs: an edit keeps its own scope, a new one follows who is creating it.
   */
  /* Deciding purely from isAdmin meant an admin editing their *own* personal pick posted scope
   * 'global', and SaveAdminPickAsync then republished it to every user and stripped its owner. The
   * list's delete path already branched on scope; only save did not. See #631. */
  readonly targetScope = (): 'global' | 'user' =>
    this.isEdit ? (this.data?.scope === 'global' ? 'global' : 'user') : this.isAdmin ? 'global' : 'user';

  get currentAlarmType(): string {
    return this.mainForm.controls.alarmType.value ?? 'monster';
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  /** The translated alarm type for the type picker; the stored value stays 'monster', 'raid' and so on. */
  alarmTypeLabel(alarmType: string): string {
    return quickPickAlarmTypeLabel(alarmType, this.i18n);
  }

  /** The stored value that means "any Pokémon" for the current type. */
  anyPokemonValue(type: string = this.currentAlarmType): number {
    return ANY_POKEMON[type] ?? 0;
  }

  /** Display label for a stored category. Unmapped values render as stored. */
  categoryLabel(category: string): string {
    const key = CATEGORY_KEYS[category];
    return key ? this.i18n.instant(key) : category;
  }

  getFilterForm(type: string): FormGroup<any> | null {
    switch (type) {
      case 'monster':
        return this.monsterForm;
      case 'raid':
        return this.raidForm;
      case 'quest':
        return this.questForm;
      case 'invasion':
        return this.invasionForm;
      case 'lure':
        return this.lureForm;
      case 'nest':
        return this.nestForm;
      case 'gym':
        return this.gymForm;
      case 'egg':
        return this.eggForm;
      case 'maxbattle':
        return this.maxBattleForm;
      default:
        return null;
    }
  }

  /** The icons to offer: the stored one first when it is not one of ours, so an edit keeps it. */
  iconChoices(): string[] {
    const current = this.mainForm.controls.icon.value ?? '';
    return current && !QUICK_PICK_ICONS.includes(current) ? [current, ...QUICK_PICK_ICONS] : QUICK_PICK_ICONS;
  }

  /** Whether a 0/1 filter is on. The stored shape stays a number; only the control is a switch. */
  isOn(form: FormGroup, key: string): boolean {
    return Number(form.get(key)?.value) === 1;
  }

  ngOnInit(): void {
    // A species picked for one type is not the next type's, and an encounter quest's species is its
    // reward: rebuild the selector so its text box shows what the new control holds.
    merge(this.mainForm.controls.alarmType.valueChanges, this.questForm.controls.rewardType.valueChanges)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.selectorKey.update(k => k + 1));

    if (this.data?.filters) {
      const filters = this.data.filters;
      const form = this.getFilterForm(this.data.alarmType);
      if (form) {
        Object.keys(filters).forEach(key => {
          const control = form.get(key);
          if (control) {
            control.setValue(filters[key]);
          }
        });
      }
    }

    // `reward` means a different thing for each reward type: an encounter's species, an item's id, an
    // amount. Kept across a change of type it was saved as the wrong thing (a Dragonite id read as item
    // 149), so a change of type starts the reward over. Subscribed after the stored filters load, so
    // opening a pick never clears its own reward.
    const reward = this.questForm.controls.reward;
    let rewardType = Number(this.questForm.controls.rewardType.value);
    this.questForm.controls.rewardType.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(value => {
      const next = Number(value);
      if (next !== rewardType) reward.setValue(0);
      rewardType = next;
    });
  }

  onPokemonPicked(ids: number[]): void {
    const control = this.pokemonControl();
    if (!control) return;
    const id = ids[ids.length - 1];
    // The selector's own "All Pokémon" entry is id 0, which a max battle stores as 9000.
    control.setValue(id === undefined || id === 0 ? this.anyPokemonValue() : id);
  }

  pickIcon(icon: string): void {
    this.mainForm.controls.icon.setValue(icon);
  }

  /**
   * The control the Pokémon picker writes, or null where applying the pick reads no species. That is
   * `pokemonId` for Pokémon, raids, nests and max battles, and for a quest the reward of an encounter
   * (reward type 7); see QuickPickService's Apply* and Build* methods.
   */
  pokemonControl() {
    if (this.currentAlarmType === 'quest') {
      return Number(this.questForm.controls.rewardType.value) === QUEST_REWARD_POKEMON ? this.questForm.controls.reward : null;
    }
    return this.getFilterForm(this.currentAlarmType)?.get('pokemonId') ?? null;
  }

  /** The stored species as the selector's starting pick; empty when the pick matches any Pokémon. */
  pokemonInitialIds(): number[] {
    const id = Number(this.pokemonControl()?.value ?? 0);
    return id && id !== this.anyPokemonValue() ? [id] : [];
  }

  /** Back to any Pokémon, and a fresh selector so its text box does not keep the old name. */
  resetPokemon(): void {
    this.pokemonControl()?.setValue(this.anyPokemonValue());
    this.selectorKey.update(k => k + 1);
  }

  save(): void {
    if (this.mainForm.invalid) return;
    this.saving.set(true);

    const main = this.mainForm.getRawValue();
    const filterForm = this.getFilterForm(main.alarmType ?? 'monster');
    // Start from what is stored rather than rebuilding: any key the dialog has no control for used to be
    // dropped on save, and quick-pick-apply reads filters['clean'] as its base bitmask while no form
    // exposes it. See #654.
    // A type change clears the old type's filters -- carrying them across stored minIv and
    // pvpRankingLeague on a lure pick (#669) -- but keeps the keys that belong to no type. Clearing
    // wholesale threw away `clean`, which quick-pick-apply reads as its base bitmask, so changing a
    // pick's type silently reset its auto-delete, edit and summary bits: the preservation #654 added,
    // undone by the fix for #669. See #671.
    const sameType = this.data?.alarmType === (main.alarmType ?? 'monster');
    const previous = this.data?.filters ?? {};
    const stored: Record<string, unknown> = sameType
      ? { ...previous }
      : Object.fromEntries(Object.entries(previous).filter(([key]) => TYPE_AGNOSTIC_FILTER_KEYS.has(key)));
    const filters: Record<string, unknown> = stored;
    if (filterForm) {
      const raw = filterForm.getRawValue();
      Object.entries(raw).forEach(([key, value]) => {
        if (value === null || value === '' || value === undefined) {
          delete filters[key];
          return;
        }

        // A stored 0 is kept, because 0 is a real filter value: the built-in nundo preset is
        // { minIv: 0, maxIv: 0 }, and dropping those turned "0% IV only" into "any IV" for everyone who
        // applied it afterwards. A 0 that was never stored is still skipped, because most of these
        // controls default to 0 meaning "not set" -- writing them out would put an explicit minIv,
        // pokemonId and pvpRankingLeague of 0 on every newly created pick.
        if (value === 0 && !(key in stored)) {
          return;
        }

        filters[key] = value;
      });
    }

    const definition: QuickPickDefinition = {
      id: this.data?.id ?? '',
      name: main.name ?? '',
      alarmType: main.alarmType ?? 'monster',
      category: main.category ?? 'Common',
      description: main.description ?? '',
      enabled: main.enabled ?? true,
      filters,
      icon: main.icon ?? 'bolt',
      scope: this.targetScope(),
      sortOrder: main.sortOrder ?? 0,
    };

    const obs = this.targetScope() === 'global' ? this.quickPickService.saveAdmin(definition) : this.quickPickService.saveUser(definition);

    obs.subscribe({
      error: () => {
        this.snackBar.open(this.i18n.instant('QUICK_PICKS.SNACK_FAILED_SAVE'), this.i18n.instant('TOAST.OK'), {
          duration: 3000,
        });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open(
          this.i18n.instant(this.isEdit ? 'QUICK_PICKS.SNACK_UPDATED' : 'QUICK_PICKS.SNACK_CREATED_QP'),
          this.i18n.instant('TOAST.OK'),
          { duration: 3000 },
        );
        this.dialogRef.close(true);
      },
    });
  }

  /** Sets a 0/1 filter from its switch. */
  setOn(form: FormGroup, key: string, on: boolean): void {
    form.get(key)?.setValue(on ? 1 : 0);
  }
}
