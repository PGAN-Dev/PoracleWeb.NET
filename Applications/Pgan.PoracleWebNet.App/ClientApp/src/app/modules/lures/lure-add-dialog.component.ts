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
import { TranslateModule } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';

import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { LureService } from '../../core/services/lure.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

interface LureOption {
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
    TranslateModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-lure-add-dialog',
  standalone: true,
  styleUrl: './lure-add-dialog.component.scss',
  templateUrl: './lure-add-dialog.component.html',
})
export class LureAddDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly lureService = inject(LureService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<LureAddDialogComponent>);
  form = this.fb.group({ clean: [false], distanceKm: [1], distanceMode: ['areas' as 'areas' | 'distance'], ping: [''], template: [''] });
  readonly isWebhook = inject(AuthService).isImpersonating();
  lureTypes: LureOption[] = [
    { id: 501, name: 'Normal', color: '#FF9800' },
    { id: 502, name: 'Glacial', color: '#03A9F4' },
    { id: 503, name: 'Mossy', color: '#4CAF50' },
    { id: 504, name: 'Magnetic', color: '#9E9E9E' },
    { id: 505, name: 'Rainy', color: '#2196F3' },
    { id: 506, name: 'Golden', color: '#FFC107' },
  ];

  saving = signal(false);
  selectedLureIds = signal<number[]>([]);

  getLureIcon(lureId: number): string {
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/reward/item/${lureId}.png`;
  }

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0);
    else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1);
  }

  save(): void {
    if (this.selectedLureIds().length === 0) return;
    this.saving.set(true);
    const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    const creates = this.selectedLureIds().map(lureId =>
      this.lureService.create({
        clean: v.clean ? 1 : 0,
        distance: dist,
        lureId,
        ping: v.ping || null,
        template: v.template || null,
      }),
    );
    forkJoin(creates).subscribe({
      error: () => {
        this.snackBar.open(this.i18n.instant('LURES.SNACK_FAILED_CREATE'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open(this.i18n.instant('LURES.SNACK_CREATED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }

  toggleLure(id: number): void {
    this.selectedLureIds.update(ids => (ids.includes(id) ? ids.filter(i => i !== id) : [...ids, id]));
  }
}
