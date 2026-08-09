import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe } from '@ngx-translate/core';

import { QuickPickDefinition } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { QuickPickService } from '../../core/services/quick-pick.service';

@Component({
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    TranslatePipe,
  ],
  selector: 'app-quick-pick-admin-dialog',
  standalone: true,
  styleUrl: './quick-pick-admin-dialog.component.scss',
  templateUrl: './quick-pick-admin-dialog.component.html',
})
export class QuickPickAdminDialogComponent implements OnInit {
  private readonly auth = inject(AuthService);
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
    pvpRankingBest: [0],
    pvpRankingLeague: [0],
    pvpRankingMinCp: [0],
    pvpRankingWorst: [4096],
    size: [-1],
  });

  nestForm = this.fb.group({
    minSpawnAvg: [0],
    pokemonId: [0],
  });

  questForm = this.fb.group({
    pokemonId: [0],
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

  ngOnInit(): void {
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
  }

  save(): void {
    if (this.mainForm.invalid) return;
    this.saving.set(true);

    const main = this.mainForm.getRawValue();
    const filterForm = this.getFilterForm(main.alarmType ?? 'monster');
    // Start from what is stored rather than rebuilding: any key the dialog has no control for used to be
    // dropped on save, and quick-pick-apply reads filters['clean'] as its base bitmask while no form
    // exposes it. See #654.
    // Only when the type is unchanged. alarmType is editable on the edit dialog, and carrying the old
    // type's keys across stored minIv and pvpRankingLeague on a lure pick. Inert at apply time, since
    // BuildFromFilters ignores unknown keys, but it is junk in the definition. See #669.
    const sameType = this.data?.alarmType === (main.alarmType ?? 'monster');
    const stored: Record<string, unknown> = sameType ? { ...(this.data?.filters ?? {}) } : {};
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
          this.i18n.instant(this.isEdit ? 'QUICK_PICKS.SNACK_UPDATED' : 'QUICK_PICKS.SNACK_CREATED'),
          this.i18n.instant('TOAST.OK'),
          { duration: 3000 },
        );
        this.dialogRef.close(true);
      },
    });
  }
}
