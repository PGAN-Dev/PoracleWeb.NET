import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslatePipe } from '@ngx-translate/core';
import { catchError, forkJoin, of } from 'rxjs';

import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { QuestService } from '../../core/services/quest.service';
import { SummaryScheduleService } from '../../core/services/summary-schedule.service';
import { PokemonSelectorComponent } from '../../shared/components/pokemon-selector/pokemon-selector.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { AlarmScope, scopeToFields } from '../../shared/utils/alarm-scope';
import { compose } from '../../shared/utils/clean-flags';

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
    MatTabsModule,
    MatRadioModule,
    MatSnackBarModule,
    TranslatePipe,
    PokemonSelectorComponent,
    TemplateSelectorComponent,
    ScopePickerComponent,
  ],
  selector: 'app-quest-add-dialog',
  standalone: true,
  styleUrl: './quest-add-dialog.component.scss',
  templateUrl: './quest-add-dialog.component.html',
})
export class QuestAddDialogComponent {
  private static readonly FALLBACK_ICON =
    "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%23999'%3E%3Cpath d='M11 18h2v-2h-2v2zm1-16C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm0-14c-2.21 0-4 1.79-4 4h2c0-1.1.9-2 2-2s2 .9 2 2c0 2-3 1.75-3 5h2c0-2.25 3-2.5 3-5 0-2.21-1.79-4-4-4z'/%3E%3C/svg%3E";

  private readonly alertDefaults = inject(AlertDefaultsService);

  private readonly fb = inject(FormBuilder);

  private readonly i18n = inject(I18nService);
  private readonly masterData = inject(MasterDataService);
  private readonly questService = inject(QuestService);
  private readonly snackBar = inject(MatSnackBar);
  commonForm = this.fb.group({
    clean: [false],
    summary: [false],
    template: [''],
  });

  readonly dialogRef = inject(MatDialogRef<QuestAddDialogComponent>);

  readonly iconService = inject(IconService);

  readonly isWebhook = inject(AuthService).isImpersonating();

  itemForm = this.fb.group({
    reward: [0],
  });

  /** Quest-relevant items (balls, berries, potions, revives, TMs, etc.) */
  readonly questItems = signal<{ id: number; name: string }[]>([]);

  saving = signal(false);

  /**
   * Seeded from the saved defaults so the Alert Defaults preference still reaches new alarms; the
   * picker owns it from there.
   */
  readonly scope = signal<AlarmScope>(
    this.alertDefaults.defaultMode() === 'areas'
      ? { mode: 'profile' }
      : {
          distanceKm: this.alertDefaults.defaultDistanceKm(),
          mode: this.alertDefaults.defaultPlaceLabel() ? 'place' : 'profile',
          placeLabel: this.alertDefaults.defaultPlaceLabel(),
        },
  );

  selectedCandyPokemonIds = signal<number[]>([]);
  selectedMegaPokemonIds = signal<number[]>([]);

  selectedPokemonIds = signal<number[]>([]);

  readonly summaryService = inject(SummaryScheduleService);

  tabIndex = 0;

  constructor() {
    this.masterData.loadData().subscribe(() => {
      // Filter to quest-relevant items (exclude tickets, passes, storage, etc.)
      const excluded = new Set([
        800, 801, 802, 803, 901, 902, 903, 904, 905, 1001, 1002, 1003, 1401, 1402, 1405, 1406, 1407, 1408, 1410, 1411,
      ]);
      this.questItems.set(
        this.masterData.getAllItems().filter(i => !excluded.has(i.id) && !i.name.includes('Ticket') && !i.name.includes('Pass Points')),
      );
    });
  }

  canSave(): boolean {
    switch (this.tabIndex) {
      case 0:
        return this.selectedPokemonIds().length > 0;
      case 1:
        return true;
      case 2:
        return this.selectedMegaPokemonIds().length > 0;
      case 3:
        return this.selectedCandyPokemonIds().length > 0;
      default:
        return false;
    }
  }

  onCandyPokemonSelected(ids: number[]): void {
    this.selectedCandyPokemonIds.set(ids);
  }

  onMegaPokemonSelected(ids: number[]): void {
    this.selectedMegaPokemonIds.set(ids);
  }

  onOptionIconError(event: Event): void {
    const img = event.target as HTMLImageElement;
    img.src = QuestAddDialogComponent.FALLBACK_ICON;
  }

  onPokemonSelected(ids: number[]): void {
    this.selectedPokemonIds.set(ids);
  }

  save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);
    const common = this.commonForm.getRawValue();
    const scope = scopeToFields(this.scope());
    // New alarms have no prior bits, so compose directly from the two surfaced toggles (edit-in-place unsupported for quests).
    const cleanValue = compose(!!common.clean, false, !!common.summary);

    const creates: ReturnType<typeof this.questService.create>[] = [];

    switch (this.tabIndex) {
      case 0:
        for (const pokemonId of this.selectedPokemonIds()) {
          creates.push(
            this.questService.create({
              overrideAreas: scope.overrideAreas,
              overrideLocationLabel: scope.overrideLocationLabel,
              clean: cleanValue,
              distance: scope.distance,
              pokemonId,
              reward: pokemonId,
              rewardType: 7,
              shiny: 0,
              template: common.template || null,
            }),
          );
        }
        break;
      case 1:
        creates.push(
          this.questService.create({
            overrideAreas: scope.overrideAreas,
            overrideLocationLabel: scope.overrideLocationLabel,
            clean: cleanValue,
            distance: scope.distance,
            pokemonId: 0,
            reward: this.itemForm.controls.reward.value ?? 0,
            rewardType: 2,
            shiny: 0,
            template: common.template || null,
          }),
        );
        break;
      case 2:
        for (const pokemonId of this.selectedMegaPokemonIds()) {
          creates.push(
            this.questService.create({
              overrideAreas: scope.overrideAreas,
              overrideLocationLabel: scope.overrideLocationLabel,
              clean: cleanValue,
              distance: scope.distance,
              pokemonId,
              reward: pokemonId,
              rewardType: 12,
              shiny: 0,
              template: common.template || null,
            }),
          );
        }
        break;
      case 3:
        for (const pokemonId of this.selectedCandyPokemonIds()) {
          creates.push(
            this.questService.create({
              overrideAreas: scope.overrideAreas,
              overrideLocationLabel: scope.overrideLocationLabel,
              clean: cleanValue,
              distance: scope.distance,
              pokemonId,
              reward: pokemonId,
              rewardType: 4,
              shiny: 0,
              template: common.template || null,
            }),
          );
        }
        break;
    }

    // forkJoin fails fast, so one refused alarm aborted the whole batch: the creates that had already
    // succeeded were never reported, the dialog stayed open and the list never reloaded. Each request
    // settles on its own now, and the toast says how many landed. See #577.
    forkJoin(creates.map(c => c.pipe(catchError((err: { error?: { error?: string } }) => of({ failed: err }))))).subscribe({
      // The server names what is wrong -- which alarm already uses these settings, which
      // field a file got wrong. A fixed string threw that away. See #567, #568.
      // Each create settles on its own, so a refused one no longer hides the ones that landed.
      // The first refusal's message is shown, because it names what is in the way. See #577.
      next: (results: ({ uid?: number } | { failed: { error?: { error?: string } } })[]) => {
        const refused = results.filter((r): r is { failed: { error?: { error?: string } } } => 'failed' in r);
        // Three outcomes, not two: refused (409), already tracked (200 with no uid), and created. The
        // pokemon dialog has split these since #495; the rest reported duplicates as creations. See #605.
        const landed = results.filter((r): r is { uid?: number } => !('failed' in r));
        const created = landed.filter(r => (r.uid ?? 0) > 0).length;
        const duplicates = landed.length - created;
        this.saving.set(false);

        if (refused.length > 0) {
          this.snackBar.open(
            refused[0].failed?.error?.error ?? this.i18n.instant('QUESTS.SNACK_FAILED_CREATE'),
            this.i18n.instant('COMMON.OK'),
            { duration: 6000 },
          );
        } else {
          const message =
            duplicates > 0
              ? this.i18n.instant('ALARM.SNACK_CREATED_WITH_DUPLICATES', { count: created, duplicates })
              : this.i18n.instant('COMMON.SAVED', { count: created });
          this.snackBar.open(message, this.i18n.instant('COMMON.OK'), { duration: 4000 });
        }

        // Close either way: whatever was created is real, and the list must reload to show it.
        this.dialogRef.close(true);
      },
    });
  }
}
