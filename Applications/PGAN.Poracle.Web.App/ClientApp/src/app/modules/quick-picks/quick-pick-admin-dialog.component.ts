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

import { QuickPickDefinition } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
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
  ],
  selector: 'app-quick-pick-admin-dialog',
  standalone: true,
  styleUrl: './quick-pick-admin-dialog.component.scss',
  templateUrl: './quick-pick-admin-dialog.component.html',
})
export class QuickPickAdminDialogComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly quickPickService = inject(QuickPickService);
  private readonly snackBar = inject(MatSnackBar);
  readonly alarmTypes = ['monster', 'raid', 'egg', 'quest', 'invasion', 'lure', 'nest', 'gym'];
  readonly categories = ['Common', 'PvP', 'Size', 'Raids', 'Quests', 'Invasions', 'Custom'];
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

  monsterForm = this.fb.group({
    form: [0],
    gender: [0],
    maxCp: [9000],
    maxIv: [100],
    maxLevel: [40],
    maxSize: [0],
    maxWeight: [9000000],
    minCp: [0],
    minIv: [0],
    minLevel: [0],
    minWeight: [0],
    pokemonId: [0],
    pvpRankingBest: [1],
    pvpRankingLeague: [0],
    pvpRankingMinCp: [0],
    pvpRankingWorst: [100],
    size: [0],
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
    const filters: Record<string, unknown> = {};
    if (filterForm) {
      const raw = filterForm.getRawValue();
      Object.entries(raw).forEach(([key, value]) => {
        if (value !== 0 && value !== '' && value !== null) {
          filters[key] = value;
        }
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
      scope: this.isAdmin ? 'global' : 'user',
      sortOrder: main.sortOrder ?? 0,
    };

    const obs = this.isAdmin ? this.quickPickService.saveAdmin(definition) : this.quickPickService.saveUser(definition);

    obs.subscribe({
      error: () => {
        this.snackBar.open('Failed to save quick pick', 'OK', {
          duration: 3000,
        });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open(`Quick pick ${this.isEdit ? 'updated' : 'created'}`, 'OK', { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }
}
