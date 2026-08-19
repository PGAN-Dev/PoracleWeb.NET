import { ChangeDetectionStrategy, Component, Injector, OnInit, computed, effect, inject, input, model, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

import { AreaService } from '../../../core/services/area.service';
import { PlacesService } from '../../../core/services/places.service';
import { UserGeofenceService } from '../../../core/services/user-geofence.service';
import { AlarmScope, titleCaseArea } from '../../utils/alarm-scope';
import { ConfirmDialogComponent } from '../confirm-dialog/confirm-dialog.component';
import { LocationDialogComponent } from '../location-dialog/location-dialog.component';

/**
 * What the picker offers, which is not quite what PoracleNG stores. "Near a point" covers both a
 * radius from the profile pin and a radius from a saved place, because to a person those are one
 * choice with a target rather than two unrelated modes.
 */
type PickerMode = 'areas' | 'inherit' | 'near';

/**
 * Where an alarm reaches you. The one control for that decision, wherever it is asked.
 *
 * It used to be asked twice in two different shapes: a two-option radio inside the alarm dialogs, and
 * a three-option sheet from the card chip. That was not only inconsistent, it was lossy — the dialog
 * version had no "only in specific areas", so a per-alarm area override could not be set until after
 * the alarm existed. The two drifted apart within a day of being written, which is the argument for a
 * shared component rather than two carefully-matched copies.
 *
 * The three options are a radio group because PoracleNG treats them as mutually exclusive: a place
 * with areas, areas with a radius, or a place without one are all refused. Modelled as a choice,
 * those states cannot be expressed, so there is nothing to validate and no error copy to write.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatRadioModule,
    MatSelectModule,
    TranslatePipe,
  ],
  selector: 'app-scope-picker',
  standalone: true,
  styleUrl: './scope-picker.component.scss',
  templateUrl: './scope-picker.component.html',
})
export class ScopePickerComponent implements OnInit {
  private readonly areaService = inject(AreaService);
  private readonly dialog = inject(MatDialog);
  private readonly geofenceService = inject(UserGeofenceService);
  private readonly injector = inject(Injector);
  private readonly translate = inject(TranslateService);

  /**
   * Sentinel for the "add a place" row. Creating a place is only ever wanted at this exact moment,
   * and sending someone to another screen to do it lost the alarm they were editing.
   */
  protected readonly ADD_PLACE = '__add-place__';

  /**
   * Admin areas plus the user's own geofences. Their own are listed because PoracleWeb writes them
   * past PoracleNG's user-selectable filter; without that they would be offered and then refused.
   */
  readonly availableAreas = signal<{ name: string; own: boolean }[]>([]);

  readonly distanceKm = signal<number>(1);

  /** The dialog puts the same question in its title bar, so it suppresses the inline one. */
  readonly hideHeading = input(false);

  readonly mode = signal<PickerMode>('inherit');

  /** Empty means the profile pin; anything else is a saved place's label. */
  readonly placeLabel = signal<string>('');

  readonly places = inject(PlacesService);

  /**
   * True when the alarm would measure from a pin the user has never set. PoracleNG falls back to 0,0
   * and alerts on nothing useful, so this is worth saying at the moment the choice is made rather
   * than leaving someone to wonder why an alarm is silent.
   */
  readonly pinMissing = computed(() => this.mode() === 'near' && !this.placeLabel() && !this.places.pin());

  /** Areas the profile subscribes to, so the inherited option can say what it means. */
  readonly profileAreas = input<string[]>([]);

  readonly profileAreaSummary = computed(() => this.profileAreas().map(titleCaseArea).join(', '));

  /** The alarm's scope. Two-way, so a host can seed it and read it back without an event dance. */
  readonly scope = model<AlarmScope>({ distanceKm: 1, mode: 'profile' });

  readonly selectedAreas = signal<string[]>([]);

  ngOnInit(): void {
    // Seed here, not in the constructor: a signal input is not populated until after construction, so
    // reading it there got the model's own default and wrote it straight back over whatever the host
    // passed. That silently discarded the Alert Defaults preference on a new alarm and an existing
    // alarm's own scope when editing one.
    const initial = this.scope();
    this.mode.set(initialMode(initial));
    this.placeLabel.set(initial.placeLabel ?? '');
    this.distanceKm.set(initial.distanceKm || 1);
    this.selectedAreas.set(initial.areas ?? []);

    // Only mirror outward after seeding, or the write-back races the seed.
    effect(() => this.scope.set(this.currentScope()), { injector: this.injector });

    this.places.load().subscribe({ error: () => undefined });

    this.areaService.getAvailable().subscribe({
      error: () => undefined,
      next: areas => this.availableAreas.update(current => [...areas.map(a => ({ name: a.name, own: false })), ...current]),
    });

    this.geofenceService.getCustomGeofences().subscribe({
      error: () => undefined,
      next: own => this.availableAreas.update(current => [...current, ...own.map(g => ({ name: g.kojiName, own: true }))]),
    });
  }

  /** Opens the place picker when the add row is chosen, and selects whatever comes back. */
  onTargetChange(value: string): void {
    if (value !== this.ADD_PLACE) {
      this.placeLabel.set(value);
      return;
    }

    // Seed with the pin so the map opens somewhere recognisable. 0,0 put it in the Atlantic.
    const anchor = this.places.pin();

    this.dialog
      .open(LocationDialogComponent, {
        data: { latitude: anchor?.latitude ?? 0, longitude: anchor?.longitude ?? 0, pickOnly: true },
      })
      .afterClosed()
      .subscribe((point?: { latitude: number; longitude: number }) => {
        if (point) this.namePlace(point);
      });
  }

  /**
   * Sets the profile pin from here. Sending someone to Areas & Places would lose the alarm they are in
   * the middle of, and this is the moment they find out they need one.
   */
  setPin(): void {
    this.dialog
      .open(LocationDialogComponent, { data: this.places.pin() })
      .afterClosed()
      .subscribe((saved?: { latitude: number; longitude: number }) => {
        // The dialog writes the pin itself; re-reading is what clears the warning.
        if (saved) this.places.load().subscribe({ error: () => undefined });
      });
  }

  protected titleCase(area: string): string {
    return titleCaseArea(area);
  }

  private currentScope(): AlarmScope {
    switch (this.mode()) {
      case 'areas':
        return { areas: this.selectedAreas(), mode: 'areas' };
      case 'near':
        return this.placeLabel()
          ? { distanceKm: this.distanceKm(), mode: 'place', placeLabel: this.placeLabel() }
          : { distanceKm: this.distanceKm(), mode: 'profile' };
      default:
        return { mode: 'profile' };
    }
  }

  private namePlace(point: { latitude: number; longitude: number }): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        width: '420px',
        data: {
          confirmText: this.translate.instant('COMMON.SAVE'),
          message: this.translate.instant('WHERE.NAME_PLACE_MESSAGE'),
          promptField: {
            existingNames: this.places.named().map(p => p.label),
            label: this.translate.instant('WHERE.PLACE_NAME'),
            value: '',
          },
          title: this.translate.instant('WHERE.NAME_PLACE_TITLE'),
        },
      })
      .afterClosed()
      .subscribe((label?: false | string) => {
        if (!label) return;

        this.places.add({ label, latitude: point.latitude, longitude: point.longitude }).subscribe({
          error: () => undefined,
          // Select it straight away: the only reason to make a place here is to use it here.
          next: () => this.placeLabel.set(label),
        });
      });
  }
}

/** A stored scope back into the picker's three options. A pin radius lands on "near", not "inherit". */
function initialMode(scope: AlarmScope): PickerMode {
  if (scope.mode === 'areas') return 'areas';
  if (scope.mode === 'place') return 'near';
  return (scope.distanceKm ?? 0) > 0 ? 'near' : 'inherit';
}
