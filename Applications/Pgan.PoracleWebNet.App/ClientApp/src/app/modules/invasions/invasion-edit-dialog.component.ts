import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';

import { Invasion, InvasionUpdate } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { InvasionService } from '../../core/services/invasion.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

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
    MatRadioModule,
    MatTabsModule,
    MatSnackBarModule,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-invasion-edit-dialog',
  standalone: true,
  styleUrl: './invasion-edit-dialog.component.scss',
  templateUrl: './invasion-edit-dialog.component.html',
})
export class InvasionEditDialogComponent {
  private static readonly DISPLAY_NAMES: Record<string, string> = {
    decoy: 'Decoy Grunt',
    everything: 'All Invasions',
    giovanni: 'Giovanni',
    'gold-stop': 'Gold Stop',
    kecleon: 'Kecleon',
    metal: 'Steel',
    mixed: 'Rocket Leader',
    showcase: 'Showcase',
  };

  private static readonly EVENT_TYPE_INFO: Record<string, { color: string; icon: string; imgUrl?: string }> = {
    'gold-stop': { color: '#F9E418', icon: 'paid' },
    kecleon: {
      color: '#B3CA78',
      icon: 'visibility_off',
      imgUrl: 'https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/pokemon/352.png',
    },
    showcase: { color: '#03AEB6', icon: 'emoji_events' },
  };

  private static readonly INVASION_ID: Record<string, number> = {
    decoy: 50,
    giovanni: 44,
    mixed: 41,
  };

  private static readonly TYPE_ID: Record<string, number> = {
    bug: 7,
    dark: 17,
    dragon: 16,
    electric: 13,
    fairy: 18,
    fighting: 2,
    fire: 10,
    flying: 3,
    ghost: 8,
    grass: 12,
    ground: 5,
    ice: 15,
    metal: 9,
    normal: 1,
    poison: 4,
    psychic: 14,
    rock: 6,
    water: 11,
  };

  private static readonly UICONS = 'https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons';
  private readonly fb = inject(FormBuilder);
  private readonly invasionService = inject(InvasionService);
  private readonly snackBar = inject(MatSnackBar);

  readonly data = inject<Invasion>(MAT_DIALOG_DATA);

  readonly dialogRef = inject(MatDialogRef<InvasionEditDialogComponent>);
  form = this.fb.group({
    clean: [this.data.clean === 1],
    distanceKm: [this.data.distance > 0 ? this.data.distance / 1000 : 1],
    distanceMode: [this.data.distance === 0 ? 'areas' : ('distance' as 'areas' | 'distance')],
    gender: [this.data.gender],
    ping: [this.data.ping ?? ''],
    template: [this.data.template ?? ''],
  });

  readonly isEvent = (this.data.gruntType ?? '') in InvasionEditDialogComponent.EVENT_TYPE_INFO;
  readonly isWebhook = inject(AuthService).isImpersonating();

  saving = signal(false);

  getDisplayName(): string {
    const type = this.data.gruntType;
    if (!type) return 'Unknown Grunt';
    const mapped = InvasionEditDialogComponent.DISPLAY_NAMES[type];
    if (mapped) return mapped;
    return type.charAt(0).toUpperCase() + type.slice(1);
  }

  getEventColor(): string {
    return InvasionEditDialogComponent.EVENT_TYPE_INFO[this.data.gruntType ?? '']?.color ?? '';
  }

  getEventIcon(): string {
    return InvasionEditDialogComponent.EVENT_TYPE_INFO[this.data.gruntType ?? '']?.icon ?? '';
  }

  getEventImgUrl(): string {
    return InvasionEditDialogComponent.EVENT_TYPE_INFO[this.data.gruntType ?? '']?.imgUrl ?? '';
  }

  getGenderLabel(): string {
    switch (this.data.gender) {
      case 1:
        return 'Male';
      case 2:
        return 'Female';
      default:
        return 'Any Gender';
    }
  }

  getGruntIcon(): string {
    const type = this.data.gruntType ?? '';
    const typeId = InvasionEditDialogComponent.TYPE_ID[type];
    if (typeId) return `${InvasionEditDialogComponent.UICONS}/type/${typeId}.png`;
    const invasionId = InvasionEditDialogComponent.INVASION_ID[type];
    if (invasionId) return `${InvasionEditDialogComponent.UICONS}/invasion/${invasionId}.png`;
    return '';
  }

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0);
    else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1);
  }

  save(): void {
    this.saving.set(true);
    const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    this.invasionService
      .update(this.data.uid, {
        clean: v.clean ? 1 : 0,
        distance: dist,
        gender: v.gender ?? 0,
        gruntType: this.data.gruntType ?? '',
        ping: v.ping || null,
        template: v.template || null,
      } as InvasionUpdate)
      .subscribe({
        error: () => {
          this.snackBar.open('Failed to update alarm', 'OK', { duration: 3000 });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open('Invasion alarm updated', 'OK', { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
