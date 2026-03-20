import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { forkJoin } from 'rxjs';

import { AuthService } from '../../core/services/auth.service';
import { InvasionService } from '../../core/services/invasion.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

interface GruntOption {
  invasionId: number;
  key: string;
  name: string;
  selected: boolean;
  typeId: number;
}

const UICONS_BASE = 'https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons';

@Component({
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatIconModule,
    MatCheckboxModule,
    MatRadioModule,
    MatTabsModule,
    MatSnackBarModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-invasion-add-dialog',
  standalone: true,
  styleUrl: './invasion-add-dialog.component.scss',
  templateUrl: './invasion-add-dialog.component.html',
})
export class InvasionAddDialogComponent implements OnInit {
  private static readonly GRUNT_TYPES: { key: string; name: string; typeId: number; invasionId: number }[] = [
    { name: 'Bug', invasionId: 1, key: 'Bug', typeId: 7 },
    { name: 'Dark', invasionId: 2, key: 'Dark', typeId: 17 },
    { name: 'Dragon', invasionId: 3, key: 'Dragon', typeId: 16 },
    { name: 'Electric', invasionId: 4, key: 'Electric', typeId: 13 },
    { name: 'Fairy', invasionId: 5, key: 'Fairy', typeId: 18 },
    { name: 'Fighting', invasionId: 6, key: 'Fighting', typeId: 2 },
    { name: 'Fire', invasionId: 7, key: 'Fire', typeId: 10 },
    { name: 'Flying', invasionId: 8, key: 'Flying', typeId: 3 },
    { name: 'Ghost', invasionId: 9, key: 'Ghost', typeId: 8 },
    { name: 'Grass', invasionId: 10, key: 'Grass', typeId: 12 },
    { name: 'Ground', invasionId: 11, key: 'Ground', typeId: 5 },
    { name: 'Ice', invasionId: 12, key: 'Ice', typeId: 15 },
    { name: 'Steel', invasionId: 13, key: 'Metal', typeId: 9 },
    { name: 'Normal', invasionId: 14, key: 'Normal', typeId: 1 },
    { name: 'Poison', invasionId: 15, key: 'Poison', typeId: 4 },
    { name: 'Psychic', invasionId: 16, key: 'Psychic', typeId: 14 },
    { name: 'Rock', invasionId: 17, key: 'Rock', typeId: 6 },
    { name: 'Water', invasionId: 18, key: 'Water', typeId: 11 },
    { name: 'Rocket Leader', invasionId: 41, key: 'mixed', typeId: 0 },
    { name: 'Giovanni', invasionId: 44, key: 'Giovanni', typeId: 0 },
    { name: 'Decoy Grunt', invasionId: 50, key: 'Decoy', typeId: 0 },
  ];

  private readonly fb = inject(FormBuilder);
  private readonly invasionService = inject(InvasionService);
  private readonly masterData = inject(MasterDataService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<InvasionAddDialogComponent>);

  form = this.fb.group({
    clean: [false],
    distanceKm: [1],
    distanceMode: ['areas' as 'areas' | 'distance'],
    gender: [0],
    ping: [''],
    template: [''],
  });

  gruntOptions = signal<GruntOption[]>([]);
  readonly isWebhook = inject(AuthService).isImpersonating();
  saving = signal(false);
  selectedCount = signal(0);

  readonly trackAll = signal(false);

  canSave(): boolean {
    return this.trackAll() || this.selectedCount() > 0;
  }

  getGruntIcon(grunt: GruntOption): string {
    if (grunt.typeId > 0) {
      return `${UICONS_BASE}/type/${grunt.typeId}.png`;
    }
    return `${UICONS_BASE}/invasion/${grunt.invasionId}.png`;
  }

  ngOnInit(): void {
    this.gruntOptions.set(InvasionAddDialogComponent.GRUNT_TYPES.map(g => ({ ...g, selected: false })));
  }

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0);
    else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1);
  }

  save(): void {
    if (!this.canSave()) return;

    if (this.trackAll()) {
      this.saving.set(true);
      const v = this.form.getRawValue();
      const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
      this.invasionService
        .create({
          clean: v.clean ? 1 : 0,
          distance: dist,
          gender: v.gender ?? 0,
          gruntType: null,
          ping: v.ping || null,
          profileNo: 1,
          template: v.template || null,
        })
        .subscribe({
          error: () => {
            this.snackBar.open('Failed to create alarm', 'OK', { duration: 3000 });
            this.saving.set(false);
          },
          next: () => {
            this.snackBar.open('All invasions alarm created', 'OK', { duration: 3000 });
            this.dialogRef.close(true);
          },
        });
      return;
    }

    const selected = this.gruntOptions().filter(o => o.selected);
    if (selected.length === 0) return;
    this.saving.set(true);
    const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    const creates = selected.map(g =>
      this.invasionService.create({
        clean: v.clean ? 1 : 0,
        distance: dist,
        gender: v.gender ?? 0,
        gruntType: g.key,
        ping: v.ping || null,
        profileNo: 1,
        template: v.template || null,
      }),
    );
    forkJoin(creates).subscribe({
      error: () => {
        this.snackBar.open('Failed to create alarm(s)', 'OK', { duration: 3000 });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open(`${creates.length} invasion alarm(s) created`, 'OK', { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }

  toggleGrunt(key: string): void {
    this.gruntOptions.update(opts => opts.map(o => (o.key === key ? { ...o, selected: !o.selected } : o)));
    this.selectedCount.set(this.gruntOptions().filter(o => o.selected).length);
  }
}
