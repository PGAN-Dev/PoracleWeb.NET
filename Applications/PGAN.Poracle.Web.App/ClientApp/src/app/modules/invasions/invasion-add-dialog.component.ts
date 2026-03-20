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
}

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

  private static readonly GRUNT_TYPES: { key: string; name: string }[] = [
    { key: 'Bug', name: 'Bug' },
    { key: 'Dark', name: 'Dark' },
    { key: 'Dragon', name: 'Dragon' },
    { key: 'Electric', name: 'Electric' },
    { key: 'Fairy', name: 'Fairy' },
    { key: 'Fighting', name: 'Fighting' },
    { key: 'Fire', name: 'Fire' },
    { key: 'Flying', name: 'Flying' },
    { key: 'Ghost', name: 'Ghost' },
    { key: 'Grass', name: 'Grass' },
    { key: 'Ground', name: 'Ground' },
    { key: 'Ice', name: 'Ice' },
    { key: 'Metal', name: 'Steel' },
    { key: 'Normal', name: 'Normal' },
    { key: 'Poison', name: 'Poison' },
    { key: 'Psychic', name: 'Psychic' },
    { key: 'Rock', name: 'Rock' },
    { key: 'Water', name: 'Water' },
    { key: 'mixed', name: 'Rocket Leader (mixed)' },
    { key: 'Giovanni', name: 'Giovanni' },
    { key: 'Decoy', name: 'Decoy Grunt' },
  ];

  ngOnInit(): void {
    this.gruntOptions.set(
      InvasionAddDialogComponent.GRUNT_TYPES.map(g => ({ ...g, selected: false })),
    );
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
