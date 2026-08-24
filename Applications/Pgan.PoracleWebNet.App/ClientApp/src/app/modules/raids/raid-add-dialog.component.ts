import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslatePipe } from '@ngx-translate/core';
import { catchError, forkJoin, Observable, of } from 'rxjs';

import { Egg, EggCreate, Raid, RaidCreate } from '../../core/models';
import { ANY_LEVEL_VALUE } from '../../core/models/raid-level.models';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { EggService } from '../../core/services/egg.service';
import { I18nService } from '../../core/services/i18n.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { RaidService } from '../../core/services/raid.service';
import { SettingsService } from '../../core/services/settings.service';
import { GymPickerComponent } from '../../shared/components/gym-picker/gym-picker.component';
import { LevelSelectorComponent } from '../../shared/components/level-selector/level-selector.component';
import { PokemonSelectorComponent } from '../../shared/components/pokemon-selector/pokemon-selector.component';
import { RsvpToggleComponent } from '../../shared/components/rsvp-toggle/rsvp-toggle.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { AlarmScope, scopeToFields } from '../../shared/utils/alarm-scope';
import { AUTO_DELETE, EDIT } from '../../shared/utils/clean-flags';
import { ANY_COSTUME, costumeHintKey } from '../../shared/utils/costumes';

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
    MatProgressSpinnerModule,
    TranslatePipe,
    PokemonSelectorComponent,
    TemplateSelectorComponent,
    GymPickerComponent,
    LevelSelectorComponent,
    RsvpToggleComponent,
    ScopePickerComponent,
  ],
  selector: 'app-raid-add-dialog',
  standalone: true,
  styleUrl: './raid-add-dialog.component.scss',
  templateUrl: './raid-add-dialog.component.html',
})
export class RaidAddDialogComponent {
  private readonly alertDefaults = inject(AlertDefaultsService);

  private readonly eggService = inject(EggService);

  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly masterData = inject(MasterDataService);
  private readonly raidService = inject(RaidService);
  private readonly settings = inject(SettingsService);
  private readonly snackBar = inject(MatSnackBar);
  commonForm = this.fb.group({
    clean: [false],
    // Only the By Boss tab renders this. A level-only rule has no boss to wear a costume, and
    // PoracleNG stores costume on level rules too, so offering it there would create a rule that is
    // silently narrower than it looks -- the by-level path hardcodes "any" instead.
    costume: [ANY_COSTUME],
    rsvpChanges: [0],
    team: [4],
    template: [''],
  });

  readonly dialogRef = inject(MatDialogRef<RaidAddDialogComponent>);

  readonly isWebhook = inject(AuthService).isImpersonating();

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

  selectedEggLevels = signal<number[]>([]);
  selectedGymId = signal<string | null>(null);

  selectedPokemonIds = signal<number[]>([]);
  selectedRaidLevels = signal<number[]>([]);

  tabIndex = 0;

  canSave(): boolean {
    if (this.tabIndex === 0) {
      return this.selectedRaidLevels().length > 0 || this.selectedEggLevels().length > 0;
    }
    return this.selectedPokemonIds().length > 0;
  }

  /** The hint under the costume select, which changes with the selection. */
  costumeHint(): string {
    return costumeHintKey(this.commonForm.controls.costume.value ?? ANY_COSTUME, this.costumeNamesAvailable());
  }

  /** Whether the masterfile's costume names loaded; drives the hint and nothing else. */
  costumeNamesAvailable(): boolean {
    return this.masterData.costumesAvailable();
  }

  /** The named costumes for the select, newest first. */
  costumeOptions(): { id: number; name: string }[] {
    return this.masterData.getCostumes();
  }

  onPokemonSelected(ids: number[]): void {
    this.selectedPokemonIds.set(ids);
  }

  /** Boss tab is single-select; the selector emits an array of length 0 or 1. */

  save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);
    const common = this.commonForm.getRawValue();
    const scope = scopeToFields(this.scope());
    // clean is a PoracleNG bitmask: bit 1 = auto-delete, bit 2 = edit-in-place, bit 4 = summary.
    // RSVP modes (1/2) need the edit bit so count changes edit the alert instead of re-sending.
    // New alarms have no prior bits, so there is nothing to preserve here.
    const clean = (common.clean ? AUTO_DELETE : 0) | ((common.rsvpChanges ?? 0) >= 1 ? EDIT : 0);

    // A union of the two return types confuses .pipe(); both are Observable of an alarm, and the batch
    // only needs to know whether each one landed. See #577.
    const creates: Observable<Raid | Egg>[] = [];

    if (this.tabIndex === 0) {
      // By Level
      for (const level of this.selectedRaidLevels()) {
        const raid: RaidCreate = {
          overrideAreas: scope.overrideAreas,
          overrideLocationLabel: scope.overrideLocationLabel,
          clean,
          // A level rule matches whatever boss hatches, so it cannot sensibly filter on a costume.
          costume: ANY_COSTUME,
          distance: scope.distance,
          evolution: 9000,
          exclusive: 0,
          form: 0,
          gymId: this.selectedGymId() ?? '',
          level,
          move: 9000,
          pokemonId: 9000,
          rsvpChanges: common.rsvpChanges ?? 0,
          team: common.team ?? 4,
          template: common.template || null,
        };
        creates.push(this.raidService.create(raid));
      }
      for (const level of this.selectedEggLevels()) {
        const egg: EggCreate = {
          overrideAreas: scope.overrideAreas,
          overrideLocationLabel: scope.overrideLocationLabel,
          clean,
          distance: scope.distance,
          exclusive: 0,
          gymId: this.selectedGymId() ?? '',
          level,
          rsvpChanges: common.rsvpChanges ?? 0,
          team: common.team ?? 4,
          template: common.template || null,
        };
        creates.push(this.eggService.create(egg));
      }
    } else {
      // By Boss. The level is always the "any" sentinel, never a chosen one: trackingRaid.go rewrites
      // level to 9000 for every alarm carrying a specific pokemon_id, so the tab used to show a level
      // picker whose value could not survive the request. See #615.
      for (const pokemonId of this.selectedPokemonIds()) {
        const raid: RaidCreate = {
          overrideAreas: scope.overrideAreas,
          overrideLocationLabel: scope.overrideLocationLabel,
          clean,
          costume: common.costume ?? ANY_COSTUME,
          distance: scope.distance,
          evolution: 9000,
          exclusive: 0,
          form: 0,
          gymId: this.selectedGymId() ?? '',
          level: ANY_LEVEL_VALUE,
          move: 9000,
          pokemonId,
          rsvpChanges: common.rsvpChanges ?? 0,
          team: common.team ?? 4,
          template: common.template || null,
        };
        creates.push(this.raidService.create(raid));
      }
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
            refused[0].failed?.error?.error ?? this.i18n.instant('RAIDS.SNACK_FAILED_CREATE'),
            this.i18n.instant('COMMON.OK'),
            { duration: 6000 },
          );
        } else {
          const message =
            duplicates > 0
              ? this.i18n.instant('ALARM.SNACK_CREATED_WITH_DUPLICATES', { count: created, duplicates })
              : this.i18n.instant('RAIDS.SNACK_CREATED_COUNT', { count: created });
          this.snackBar.open(message, this.i18n.instant('COMMON.OK'), { duration: 4000 });
        }

        // Close either way: whatever was created is real, and the list must reload to show it.
        this.dialogRef.close(true);
      },
    });
  }

  /**
   * Whether to offer the costume filter at all. False on a Poracle without the raid.costume column: it
   * takes the field, answers 200 and drops it, so the control would produce a rule that reads
   * "Halloween 2025" and matches every spawn. Unknown counts as absent.
   */
  showCostume(): boolean {
    return this.settings.supportsCostume('raid');
  }
}
