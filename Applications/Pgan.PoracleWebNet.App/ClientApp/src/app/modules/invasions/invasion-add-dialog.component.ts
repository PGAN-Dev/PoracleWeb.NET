import { Component, OnInit, computed, inject, signal } from '@angular/core';
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
import { TranslatePipe } from '@ngx-translate/core';
import { catchError, forkJoin, of } from 'rxjs';

import { getGruntDisplayName, isGenderFixed, UICONS_BASE } from './invasion.constants';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { InvasionService } from '../../core/services/invasion.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

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
    TemplateSelectorComponent,
    DeliveryPreviewComponent,
  ],
  selector: 'app-invasion-add-dialog',
  standalone: true,
  styleUrl: './invasion-add-dialog.component.scss',
  templateUrl: './invasion-add-dialog.component.html',
})
export class InvasionAddDialogComponent implements OnInit {
  private static readonly EVENT_TYPES: { color: string; icon: string; imgUrl?: string; key: string }[] = [
    { color: '#B3CA78', icon: 'visibility_off', imgUrl: `${UICONS_BASE}/pokemon/352.png`, key: 'kecleon' },
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
    { gruntType: 'metal', invasionId: 13, key: 'metal', typeId: 9 },
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
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly invasionService = inject(InvasionService);
  private readonly masterData = inject(MasterDataService);
  private readonly snackBar = inject(MatSnackBar);
  readonly dialogRef = inject(MatDialogRef<InvasionAddDialogComponent>);

  gruntOptions = signal<GruntOption[]>([]);

  readonly eventGrunts = computed(() => this.gruntOptions().filter(g => g.isEvent));
  form = this.fb.group({
    clean: [false],
    distanceKm: [this.alertDefaults.defaultDistanceKm()],
    distanceMode: [this.alertDefaults.defaultMode()],
    gender: [0],
    template: [''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();
  readonly rocketGrunts = computed(() => this.gruntOptions().filter(g => !g.isEvent));

  saving = signal(false);
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

  getGruntIcon(grunt: GruntOption): string {
    if (grunt.typeId > 0) {
      return `${UICONS_BASE}/type/${grunt.typeId}.png`;
    }
    return `${UICONS_BASE}/invasion/${grunt.invasionId}.png`;
  }

  getGruntLabel(grunt: GruntOption): string {
    return getGruntDisplayName(grunt.gruntType, grunt.gender, key => this.i18n.instant(key));
  }

  ngOnInit(): void {
    const grunts: GruntOption[] = InvasionAddDialogComponent.GRUNT_TYPES.map(g => ({
      ...g,
      isEvent: false,
      selected: false,
    }));
    const events: GruntOption[] = InvasionAddDialogComponent.EVENT_TYPES.map(e => ({
      ...e,
      gruntType: e.key,
      invasionId: 0,
      isEvent: true,
      selected: false,
      typeId: 0,
    }));
    this.gruntOptions.set([...grunts, ...events]);
  }

  onDistanceModeChange(): void {
    if (this.form.controls.distanceMode.value === 'areas') this.form.controls.distanceKm.setValue(0);
    else if (!this.form.controls.distanceKm.value) this.form.controls.distanceKm.setValue(1);
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
    const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    const creates = targets.map(g =>
      this.invasionService.create({
        clean: v.clean ? 1 : 0,
        distance: dist,
        // Split variants (Mixed Male/Female) carry an implicit gender; typed grunts
        // fall back to the user's dropdown choice; fixed-gender rows force 0.
        gender: g.gender ?? (isGenderFixed(g.gruntType) ? 0 : (v.gender ?? 0)),
        gruntType: g.gruntType,
        template: v.template || null,
      }),
    );
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
            refused[0].failed?.error?.error ?? this.i18n.instant('INVASIONS.SNACK_FAILED_CREATE'),
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

  toggleGrunt(key: string): void {
    this.gruntOptions.update(opts => opts.map(o => (o.key === key ? { ...o, selected: !o.selected } : o)));
    this.selectedCount.set(this.gruntOptions().filter(o => o.selected).length);
  }
}
