import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslateModule } from '@ngx-translate/core';

import { QuickPickApplyRequest, QuickPickSummary } from '../../core/models';
import { I18nService } from '../../core/services/i18n.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { QuickPickService } from '../../core/services/quick-pick.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { PokemonSelectorComponent } from '../../shared/components/pokemon-selector/pokemon-selector.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

@Component({
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatProgressBarModule,
    MatRadioModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    MatTabsModule,
    MatProgressSpinnerModule,
    TranslateModule,
    PokemonSelectorComponent,
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-quick-pick-apply-dialog',
  standalone: true,
  styleUrl: './quick-pick-apply-dialog.component.scss',
  templateUrl: './quick-pick-apply-dialog.component.html',
})
export class QuickPickApplyDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly masterData = inject(MasterDataService);
  private readonly quickPickService = inject(QuickPickService);
  private readonly snackBar = inject(MatSnackBar);
  /** Total Pokemon count from master data (minus the "All Pokemon" id=0 entry) */
  private readonly totalPokemon = this.masterData.getAllPokemon().length - 1;
  readonly applying = signal(false);
  readonly applyStatus = signal('');
  readonly data = inject<QuickPickSummary>(MAT_DIALOG_DATA);
  deliveryForm = this.fb.group({
    clean: [false],
    distanceKm: [0],
    distanceMode: ['areas' as 'areas' | 'distance'],
    template: [''],
  });

  readonly dialogRef = inject(MatDialogRef<QuickPickApplyDialogComponent>);
  readonly excludedPokemonIds = signal<number[]>(this.data.appliedState?.excludePokemonIds ?? []);
  readonly excludeEnabled = signal((this.data.appliedState?.excludePokemonIds?.length ?? 0) > 0);

  readonly showExclusions =
    this.data.definition.alarmType === 'monster' &&
    (this.data.definition.filters['pokemonId'] === 0 ||
      this.data.definition.filters['pokemonId'] === undefined ||
      this.data.definition.filters['pokemonId'] === null);

  /** How many individual alarms will be created if exclusions are used */
  readonly individualAlarmCount = computed(() => {
    if (!this.showExclusions || this.excludedPokemonIds().length === 0) return 0;
    return Math.max(0, this.totalPokemon - this.excludedPokemonIds().length);
  });

  readonly isReapply = !!this.data.appliedState;

  /** Whether this apply will create individual rows */
  readonly willTrackIndividually = computed(() => this.individualAlarmCount() > 0);

  apply(): void {
    this.applying.set(true);
    this.dialogRef.disableClose = true;

    if (this.willTrackIndividually()) {
      this.applyStatus.set(this.i18n.instant('QUICK_PICKS.APPLY_STATUS_CREATING', { count: this.individualAlarmCount() }));
    } else {
      this.applyStatus.set(this.i18n.instant('QUICK_PICKS.APPLY_STATUS_APPLYING'));
    }

    const delivery = this.deliveryForm.getRawValue();
    const distanceMeters = delivery.distanceMode === 'areas' ? 0 : Math.round((delivery.distanceKm ?? 1) * 1000);

    const request: QuickPickApplyRequest = {
      clean: delivery.clean ? 1 : 0,
      distance: distanceMeters,
      excludePokemonIds: this.showExclusions && this.excludeEnabled() ? this.excludedPokemonIds() : [],
      template: delivery.template || undefined,
    };

    const obs = this.isReapply
      ? this.quickPickService.reapply(this.data.definition.id, request)
      : this.quickPickService.apply(this.data.definition.id, request);

    obs.subscribe({
      error: () => {
        this.snackBar.open(this.i18n.instant('QUICK_PICKS.SNACK_FAILED_APPLY'), this.i18n.instant('TOAST.OK'), {
          duration: 3000,
        });
        this.applying.set(false);
        this.applyStatus.set('');
        this.dialogRef.disableClose = false;
      },
      next: state => {
        const count = state.trackedUids?.length ?? 0;
        this.snackBar.open(this.i18n.instant('QUICK_PICKS.SNACK_APPLIED', { count }), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        this.dialogRef.close(true);
      },
    });
  }

  onDistanceModeChange(): void {
    if (this.deliveryForm.controls.distanceMode.value === 'areas') {
      this.deliveryForm.controls.distanceKm.setValue(0);
    } else if (!this.deliveryForm.controls.distanceKm.value) {
      this.deliveryForm.controls.distanceKm.setValue(1);
    }
  }

  onExcludedPokemonChange(ids: number[]): void {
    this.excludedPokemonIds.set(ids);
  }
}
