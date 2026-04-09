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
import { TranslateModule } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';

import { UICONS_BASE } from './invasion.constants';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { InvasionService } from '../../core/services/invasion.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { DeliveryPreviewComponent } from '../../shared/components/delivery-preview/delivery-preview.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';

interface GruntOption {
  color?: string;
  icon?: string;
  imgUrl?: string;
  invasionId: number;
  isEvent?: boolean;
  key: string;
  name: string;
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
    MatSelectModule,
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
  selector: 'app-invasion-add-dialog',
  standalone: true,
  styleUrl: './invasion-add-dialog.component.scss',
  templateUrl: './invasion-add-dialog.component.html',
})
export class InvasionAddDialogComponent implements OnInit {
  private static readonly EVENT_TYPES: { color: string; icon: string; imgUrl?: string; key: string; name: string }[] = [
    { name: 'Kecleon', color: '#B3CA78', icon: 'visibility_off', imgUrl: `${UICONS_BASE}/pokemon/352.png`, key: 'kecleon' },
    { name: 'Gold Stop', color: '#F9E418', icon: 'paid', key: 'gold-stop' },
    { name: 'Showcase', color: '#03AEB6', icon: 'emoji_events', key: 'showcase' },
  ];

  private static readonly GRUNT_TYPES: { key: string; name: string; typeId: number; invasionId: number }[] = [
    { name: 'Bug', invasionId: 1, key: 'bug', typeId: 7 },
    { name: 'Dark', invasionId: 2, key: 'dark', typeId: 17 },
    { name: 'Dragon', invasionId: 3, key: 'dragon', typeId: 16 },
    { name: 'Electric', invasionId: 4, key: 'electric', typeId: 13 },
    { name: 'Fairy', invasionId: 5, key: 'fairy', typeId: 18 },
    { name: 'Fighting', invasionId: 6, key: 'fighting', typeId: 2 },
    { name: 'Fire', invasionId: 7, key: 'fire', typeId: 10 },
    { name: 'Flying', invasionId: 8, key: 'flying', typeId: 3 },
    { name: 'Ghost', invasionId: 9, key: 'ghost', typeId: 8 },
    { name: 'Grass', invasionId: 10, key: 'grass', typeId: 12 },
    { name: 'Ground', invasionId: 11, key: 'ground', typeId: 5 },
    { name: 'Ice', invasionId: 12, key: 'ice', typeId: 15 },
    { name: 'Steel', invasionId: 13, key: 'metal', typeId: 9 },
    { name: 'Normal', invasionId: 14, key: 'normal', typeId: 1 },
    { name: 'Poison', invasionId: 15, key: 'poison', typeId: 4 },
    { name: 'Psychic', invasionId: 16, key: 'psychic', typeId: 14 },
    { name: 'Rock', invasionId: 17, key: 'rock', typeId: 6 },
    { name: 'Water', invasionId: 18, key: 'water', typeId: 11 },
    { name: 'Rocket Leader', invasionId: 41, key: 'mixed', typeId: 0 },
    { name: 'Giovanni', invasionId: 44, key: 'giovanni', typeId: 0 },
    { name: 'Decoy Grunt', invasionId: 50, key: 'decoy', typeId: 0 },
  ];

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
    distanceKm: [1],
    distanceMode: ['areas' as 'areas' | 'distance'],
    gender: [0],
    ping: [''],
    template: [''],
  });

  readonly isWebhook = inject(AuthService).isImpersonating();
  readonly rocketGrunts = computed(() => this.gruntOptions().filter(g => !g.isEvent));

  saving = signal(false);
  selectedCount = signal(0);
  readonly showGender = computed(() => {
    if (this.trackAll()) return true;
    const selected = this.gruntOptions().filter(g => g.selected);
    if (selected.length === 0) return true;
    return selected.some(g => !g.isEvent);
  });

  readonly trackAll = signal(false);

  canSave(): boolean {
    return this.trackAll() || this.selectedCount() > 0;
  }

  getGruntIcon(grunt: GruntOption): string {
    if (grunt.typeId > 0) {
      return `${UICONS_BASE}/type/${grunt.typeId}.png`;
    }
    return `${UICONS_BASE}/invasion/${grunt.invasionId}.png`;
  }

  ngOnInit(): void {
    const grunts: GruntOption[] = InvasionAddDialogComponent.GRUNT_TYPES.map(g => ({
      ...g,
      isEvent: false,
      selected: false,
    }));
    const events: GruntOption[] = InvasionAddDialogComponent.EVENT_TYPES.map(e => ({
      ...e,
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

    if (this.trackAll()) {
      this.saving.set(true);
      const v = this.form.getRawValue();
      const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
      this.invasionService
        .create({
          clean: v.clean ? 1 : 0,
          distance: dist,
          gender: v.gender ?? 0,
          gruntType: null,
          ping: v.ping || null,
          template: v.template || null,
        })
        .subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_FAILED_CREATE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
            this.saving.set(false);
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_ALL_CREATED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
            this.dialogRef.close(true);
          },
        });
      return;
    }

    const selected = this.gruntOptions().filter(o => o.selected);
    if (selected.length === 0) return;
    this.saving.set(true);
    const v = this.form.getRawValue();
    const dist = v.distanceMode === 'areas' ? 0 : Math.round((v.distanceKm ?? 1) * 1000);
    const creates = selected.map(g =>
      this.invasionService.create({
        clean: v.clean ? 1 : 0,
        distance: dist,
        gender: g.isEvent ? 0 : (v.gender ?? 0),
        gruntType: g.key,
        ping: v.ping || null,
        template: v.template || null,
      }),
    );
    forkJoin(creates).subscribe({
      error: () => {
        this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_FAILED_CREATE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        this.saving.set(false);
      },
      next: () => {
        this.snackBar.open(this.i18n.instant('INVASIONS.SNACK_CREATED_COUNT', { count: creates.length }), this.i18n.instant('TOAST.OK'), {
          duration: 3000,
        });
        this.dialogRef.close(true);
      },
    });
  }

  toggleGrunt(key: string): void {
    this.gruntOptions.update(opts => opts.map(o => (o.key === key ? { ...o, selected: !o.selected } : o)));
    this.selectedCount.set(this.gruntOptions().filter(o => o.selected).length);
  }
}
