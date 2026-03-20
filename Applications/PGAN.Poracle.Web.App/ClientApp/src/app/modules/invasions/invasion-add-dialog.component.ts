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
  key: string;
  name: string;
  selected: boolean;
  typeId: number;
}

const TYPE_ICON_BASE = 'https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/type';

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

  private static readonly GRUNT_TYPES: { key: string; name: string; typeId: number }[] = [
    { key: 'Bug', name: 'Bug', typeId: 7 },
    { key: 'Dark', name: 'Dark', typeId: 17 },
    { key: 'Dragon', name: 'Dragon', typeId: 16 },
    { key: 'Electric', name: 'Electric', typeId: 13 },
    { key: 'Fairy', name: 'Fairy', typeId: 18 },
    { key: 'Fighting', name: 'Fighting', typeId: 2 },
    { key: 'Fire', name: 'Fire', typeId: 10 },
    { key: 'Flying', name: 'Flying', typeId: 3 },
    { key: 'Ghost', name: 'Ghost', typeId: 8 },
    { key: 'Grass', name: 'Grass', typeId: 12 },
    { key: 'Ground', name: 'Ground', typeId: 5 },
    { key: 'Ice', name: 'Ice', typeId: 15 },
    { key: 'Metal', name: 'Steel', typeId: 9 },
    { key: 'Normal', name: 'Normal', typeId: 1 },
    { key: 'Poison', name: 'Poison', typeId: 4 },
    { key: 'Psychic', name: 'Psychic', typeId: 14 },
    { key: 'Rock', name: 'Rock', typeId: 6 },
    { key: 'Water', name: 'Water', typeId: 11 },
    { key: 'mixed', name: 'Rocket Leader', typeId: 0 },
    { key: 'Giovanni', name: 'Giovanni', typeId: 0 },
    { key: 'Decoy', name: 'Decoy Grunt', typeId: 0 },
  ];

  ngOnInit(): void {
    this.gruntOptions.set(
      InvasionAddDialogComponent.GRUNT_TYPES.map(g => ({ ...g, selected: false })),
    );
  }

  getTypeIcon(typeId: number): string {
    if (typeId === 0) return '';
    return `${TYPE_ICON_BASE}/${typeId}.png`;
  }

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0);
    else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1);
  }

  save(): void {
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
