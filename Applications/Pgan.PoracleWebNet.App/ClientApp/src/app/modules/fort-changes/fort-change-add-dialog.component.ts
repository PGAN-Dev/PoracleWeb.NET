import { Component, inject, signal } from '@angular/core';
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

import { AuthService } from '../../core/services/auth.service';
import { FortChangeService } from '../../core/services/fort-change.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

@Component({
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatRadioModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatTabsModule,
    MatSnackBarModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-fort-change-add-dialog',
  standalone: true,
  styleUrl: './fort-change-add-dialog.component.scss',
  templateUrl: './fort-change-add-dialog.component.html',
})
export class FortChangeAddDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly fortChangeService = inject(FortChangeService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<FortChangeAddDialogComponent>);
  form = this.fb.group({
    changeTypeImageUrl: [false],
    changeTypeLocation: [true],
    changeTypeName: [true],
    changeTypeNew: [true],
    changeTypeRemoval: [true],
    clean: [false],
    distanceKm: [1],
    distanceMode: ['areas' as 'areas' | 'distance'],
    fortType: ['everything'],
    includeEmpty: [false],
    ping: [''],
    template: [''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  saving = signal(false);

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0);
    else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1);
  }

  save(): void {
    this.saving.set(true);
    const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    const changeTypes: string[] = [];
    if (v.changeTypeName) changeTypes.push('name');
    if (v.changeTypeLocation) changeTypes.push('location');
    if (v.changeTypeImageUrl) changeTypes.push('image_url');
    if (v.changeTypeRemoval) changeTypes.push('removal');
    if (v.changeTypeNew) changeTypes.push('new');

    this.fortChangeService
      .create({
        changeTypes,
        clean: v.clean ? 1 : 0,
        distance: dist,
        fortType: v.fortType,
        includeEmpty: v.includeEmpty ? 1 : 0,
        ping: v.ping || null,
        template: v.template || null,
      })
      .subscribe({
        error: () => {
          this.snackBar.open('Failed to create alarm', 'OK', { duration: 3000 });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open('Fort change alarm created', 'OK', { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
