import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';

import { getGruntDisplayName, isGenderFixed } from './invasion.constants';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { InvasionService } from '../../core/services/invasion.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { SettingsService } from '../../core/services/settings.service';
import { AddRefusalComponent } from '../../shared/components/add-refusal/add-refusal.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { TranslatePluralPipe } from '../../shared/pipes/translate-plural.pipe';
import { AddDialogData, AddRefusal, editExistingResult, ExistingRule, finishAdd, settle } from '../../shared/utils/add-outcome';
import { AlarmScope, scopeToFields } from '../../shared/utils/alarm-scope';

interface GruntOption {
  color?: string;
  // When set, this option submits a fixed gender (1=male, 2=female) and the gender
  // dropdown is suppressed for this selection. Used for mixed/decoy which users want
  // to differentiate visually (mixed male = starter line, female = Snorlax line).
  gender?: number;
  // PoracleNG grunt_type string — unique `key` differs for split male/female variants
  // but `gruntType` points to the shared PoracleNG value.
  gruntType: string;
  icon?: string;
  imgUrl?: string;
  invasionId: number;
  isEvent?: boolean;
  key: string;
  selected: boolean;
  typeId: number;
}

@Component({
  imports: [
    AddRefusalComponent,
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSlideToggleModule,
    MatIconModule,
    MatCheckboxModule,
    MatRadioModule,
    MatSelectModule,
    MatTabsModule,
    MatSnackBarModule,
    TranslatePipe,
    TranslatePluralPipe,
    TemplateSelectorComponent,
    ScopePickerComponent,
  ],
  selector: 'app-invasion-add-dialog',
  standalone: true,
  styleUrl: './invasion-add-dialog.component.scss',
  templateUrl: './invasion-add-dialog.component.html',
})
export class InvasionAddDialogComponent implements OnInit {
  private static readonly EVENT_TYPES: { color: string; icon: string; imgPath?: string; key: string }[] = [
    { color: '#B3CA78', icon: 'visibility_off', imgPath: 'pokemon/352.png', key: 'kecleon' },
    { color: '#F9E418', icon: 'paid', key: 'gold-stop' },
    { color: '#03AEB6', icon: 'emoji_events', key: 'showcase' },
  ];

  private static readonly GRUNT_TYPES: {
    gender?: number;
    gruntType: string;
    invasionId: number;
    key: string;
    typeId: number;
  }[] = [
    { gruntType: 'bug', invasionId: 1, key: 'bug', typeId: 7 },
    { gruntType: 'dark', invasionId: 2, key: 'dark', typeId: 17 },
    { gruntType: 'dragon', invasionId: 3, key: 'dragon', typeId: 16 },
    { gruntType: 'electric', invasionId: 4, key: 'electric', typeId: 13 },
    { gruntType: 'fairy', invasionId: 5, key: 'fairy', typeId: 18 },
    { gruntType: 'fighting', invasionId: 6, key: 'fighting', typeId: 2 },
    { gruntType: 'fire', invasionId: 7, key: 'fire', typeId: 10 },
    { gruntType: 'flying', invasionId: 8, key: 'flying', typeId: 3 },
    { gruntType: 'ghost', invasionId: 9, key: 'ghost', typeId: 8 },
    { gruntType: 'grass', invasionId: 10, key: 'grass', typeId: 12 },
    { gruntType: 'ground', invasionId: 11, key: 'ground', typeId: 5 },
    { gruntType: 'ice', invasionId: 12, key: 'ice', typeId: 15 },
    // Steel is `steel` in PoracleNG's grunt data; `metal` matched nothing and v2 refuses it. The key stays
    // `metal` so the toggle id and its translation are unchanged.
    { gruntType: 'steel', invasionId: 13, key: 'metal', typeId: 9 },
    { gruntType: 'normal', invasionId: 14, key: 'normal', typeId: 1 },
    { gruntType: 'poison', invasionId: 15, key: 'poison', typeId: 4 },
    { gruntType: 'psychic', invasionId: 16, key: 'psychic', typeId: 14 },
    { gruntType: 'rock', invasionId: 17, key: 'rock', typeId: 6 },
    { gruntType: 'water', invasionId: 18, key: 'water', typeId: 11 },
    { gender: 1, gruntType: 'mixed', invasionId: 4, key: 'mixed-male', typeId: 0 },
    { gender: 2, gruntType: 'mixed', invasionId: 5, key: 'mixed-female', typeId: 0 },
    { gruntType: 'darkness', invasionId: 9, key: 'darkness', typeId: 0 },
    { gruntType: 'decoy', invasionId: 46, key: 'decoy', typeId: 0 },
    { gruntType: 'cliff', invasionId: 41, key: 'cliff', typeId: 0 },
    { gruntType: 'arlo', invasionId: 42, key: 'arlo', typeId: 0 },
    { gruntType: 'sierra', invasionId: 43, key: 'sierra', typeId: 0 },
    { gruntType: 'giovanni', invasionId: 44, key: 'giovanni', typeId: 0 },
  ];

  private readonly alertDefaults = inject(AlertDefaultsService);

  private readonly data = inject<AddDialogData | null>(MAT_DIALOG_DATA, { optional: true });

  private readonly fb = inject(FormBuilder);

  private readonly i18n = inject(I18nService);
  private readonly icons = inject(IconService);
  private readonly invasionService = inject(InvasionService);
  private readonly masterData = inject(MasterDataService);
  private readonly settings = inject(SettingsService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<InvasionAddDialogComponent>);
  gruntOptions = signal<GruntOption[]>([]);

  readonly eventGrunts = computed(() => this.gruntOptions().filter(g => g.isEvent));

  form = this.fb.group({
    clean: [false],
    gender: [0],
    template: [''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();

  /**
   * Seeded from the saved defaults so the Alert Defaults preference still reaches new alarms; the
   * picker owns it from there.
   */
  /** False while the scope picker holds a radius it won't accept, which keeps Save off. */
  readonly radiusValid = signal(true);

  /** Why the last single Add was refused; shown above the actions, cleared on the next save. */
  readonly refusal = signal<AddRefusal | null>(null);

  readonly rocketGrunts = computed(() => this.gruntOptions().filter(g => !g.isEvent));

  saving = signal(false);

  readonly scope = signal<AlarmScope>(
    this.alertDefaults.defaultMode() === 'areas'
      ? { mode: 'profile' }
      : {
          distanceKm: this.alertDefaults.defaultDistanceKm(),
          mode: this.alertDefaults.defaultPlaceLabel() ? 'place' : 'profile',
          placeLabel: this.alertDefaults.defaultPlaceLabel(),
        },
  );

  selectedCount = signal(0);

  readonly trackAll = signal(false);
  // Typed grunts (fire/water/bug/etc.) keep the gender dropdown so power users can
  // narrow to one NPC gender. Mixed/Decoy/leaders/events don't — their gender is
  // either implicit in the chosen row or meaningless.
  readonly showGender = computed(() => {
    if (this.trackAll()) return true;
    const selected = this.gruntOptions().filter(g => g.selected);
    return selected.some(g => !isGenderFixed(g.gruntType));
  });

  canSave(): boolean {
    return this.trackAll() || this.selectedCount() > 0;
  }

  /** The refusal named a stored rule: close and let the list open that one for editing. */
  editExisting(rule: ExistingRule): void {
    this.dialogRef.close(editExistingResult(rule));
  }

  getGruntIcon(grunt: GruntOption): string {
    if (grunt.typeId > 0) {
      return this.icons.getTypeUrlById(grunt.typeId);
    }
    return this.icons.getInvasionUrl(grunt.invasionId);
  }

  getGruntLabel(grunt: GruntOption): string {
    return getGruntDisplayName(
      grunt.gruntType,
      grunt.gender,
      key => this.i18n.instant(key),
      this.masterData.getGruntName(grunt.gruntType, grunt.gender),
      this.masterData.isGruntNameUntranslated(grunt.gruntType, grunt.gender),
    );
  }

  ngOnInit(): void {
    const grunts: GruntOption[] = InvasionAddDialogComponent.GRUNT_TYPES.map(g => ({
      ...g,
      isEvent: false,
      selected: false,
    }));
    // Pokestop events have their own page now, and their rows are filtered out of the invasion list
    // to match. But that split only happens where the page exists: on a PoracleNG too old to serve
    // it, or with disable_showcase set, this dialog is still the only way to track a Showcase, and
    // the rows it makes still appear in the invasion list. Keep it in that case, or the feature
    // disappears entirely for those servers.
    const events: GruntOption[] = this.settings.isDisabled('disable_showcase')
      ? InvasionAddDialogComponent.EVENT_TYPES.map(e => ({
          ...e,
          gruntType: e.key,
          invasionId: 0,
          isEvent: true,
          selected: false,
          typeId: 0,
        }))
      : [];
    this.gruntOptions.set([...grunts, ...events]);
  }

  save(): void {
    if (!this.canSave()) return;

    // "Track all" used to post a single alarm with gruntType: null. PoracleNG has no catch-all --
    // it rejects an empty grunt_type with "Grunt type mandatory" -- so that call failed every time.
    // The toggle now means what its hint always claimed: one alarm per Team Rocket grunt type.
    // Pokestop events are excluded; they are not Rocket invasions and have their own section.
    const targets = this.trackAll() ? this.rocketGrunts() : this.gruntOptions().filter(o => o.selected);
    if (targets.length === 0) return;

    this.saving.set(true);
    this.refusal.set(null);
    const v = this.form.getRawValue();
    const scope = scopeToFields(this.scope());
    const creates = targets.map(g =>
      this.invasionService.create({
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        clean: v.clean ? 1 : 0,
        distance: scope.distance,
        // Split variants (Mixed Male/Female) carry an implicit gender; typed grunts
        // fall back to the user's dropdown choice; fixed-gender rows force 0.
        gender: g.gender ?? (isGenderFixed(g.gruntType) ? 0 : (v.gender ?? 0)),
        gruntType: g.gruntType,
        template: v.template || null,
      }),
    );
    // Each create settles on its own, so one refusal cannot hide the ones that landed (#577). A single
    // refused Add keeps the dialog open with the reason inline; see finishAdd.
    forkJoin(creates.map(c => settle(c))).subscribe(results => {
      this.saving.set(false);
      finishAdd(results, {
        dialogRef: this.dialogRef,
        failedKey: 'INVASIONS.SNACK_FAILED_CREATE',
        i18n: this.i18n,
        lookup: this.data?.existingRule,
        refusal: this.refusal,
        snackBar: this.snackBar,
      });
    });
  }

  toggleGrunt(key: string): void {
    this.gruntOptions.update(opts => opts.map(o => (o.key === key ? { ...o, selected: !o.selected } : o)));
    this.selectedCount.set(this.gruntOptions().filter(o => o.selected).length);
  }
}
