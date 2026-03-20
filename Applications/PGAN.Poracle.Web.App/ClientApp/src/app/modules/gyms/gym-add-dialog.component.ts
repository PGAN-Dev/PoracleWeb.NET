import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { forkJoin } from 'rxjs';

import { AuthService } from '../../core/services/auth.service';
import { GymService } from '../../core/services/gym.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

interface TeamOption {
  color: string;
  id: number;
  name: string;
}

@Component({
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSlideToggleModule,
    MatIconModule,
    MatCheckboxModule,
    MatRadioModule,
    MatTabsModule,
    MatSnackBarModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-gym-add-dialog',
  standalone: true,
  styleUrl: './gym-add-dialog.component.scss',
  templateUrl: './gym-add-dialog.component.html',
})
export class GymAddDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly gymService = inject(GymService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<GymAddDialogComponent>);
  form = this.fb.group({
    battle_changes: [false],
    clean: [false],
    distanceKm: [1],
    distanceMode: ['areas' as 'areas' | 'distance'],
    ping: [''],
    slot_changes: [false],
    template: [''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  saving = signal(false);
  selectedTeamIds = signal<number[]>([]);
  teams: TeamOption[] = [
    { id: 0, name: 'Neutral', color: '#9E9E9E' },
    { id: 1, name: 'Mystic (Blue)', color: '#2196F3' },
    { id: 2, name: 'Valor (Red)', color: '#F44336' },
    { id: 3, name: 'Instinct (Yellow)', color: '#FFC107' },
  ];

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0);
    else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1);
  }

  save(): void {
    if (this.selectedTeamIds().length === 0) return;
    this.saving.set(true);
    const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    const creates = this.selectedTeamIds().map(team =>
      this.gymService.create({
        battle_changes: v.battle_changes ? 1 : 0,
        clean: v.clean ? 1 : 0,
        distance: dist,
        gymId: null,
        ping: v.ping || null,
        profileNo: 1,
        slot_changes: v.slot_changes ? 1 : 0,
        team,
        template: v.template || null,
      }),
    );
    forkJoin(creates).subscribe({
      error: () => {
        this.snackBar.open('Failed to create alarm(s)', 'OK', { duration: 3000 });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open(`${creates.length} gym alarm(s) created`, 'OK', { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }

  toggleTeam(id: number): void {
    this.selectedTeamIds.update(ids => (ids.includes(id) ? ids.filter(i => i !== id) : [...ids, id]));
  }
}
