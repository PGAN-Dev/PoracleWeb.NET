import { ChangeDetectionStrategy, Component, Injector, OnInit, computed, effect, inject, input, model, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { catchError, forkJoin, of } from 'rxjs';

import { AreaNameService } from '../../../core/services/area-name.service';
import { AreaService } from '../../../core/services/area.service';
import { PlacesService } from '../../../core/services/places.service';
import { UserGeofenceService } from '../../../core/services/user-geofence.service';
import { AlarmScope, formatKm, MAX_RADIUS_KM, MIN_RADIUS_KM, radiusAcceptable } from '../../utils/alarm-scope';
import { PLACE_NAME_PROMPT } from '../../utils/place-label';
import { serverReason } from '../../utils/server-reason';
import { ConfirmDialogComponent } from '../confirm-dialog/confirm-dialog.component';
import { LocationDialogComponent } from '../location-dialog/location-dialog.component';
import { ScopeMapComponent } from '../scope-map/scope-map.component';

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
 * those states cannot be expressed. The radius is the one thing left to check, and the picker reports
 * it to its host through `radiusValid`.
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
    ScopeMapComponent,
    TranslatePipe,
  ],
  selector: 'app-scope-picker',
  standalone: true,
  styleUrl: './scope-picker.component.scss',
  templateUrl: './scope-picker.component.html',
})
export class ScopePickerComponent implements OnInit {
  private readonly areaNames = inject(AreaNameService);
  private readonly areaService = inject(AreaService);
  private readonly dialog = inject(MatDialog);
  private readonly geofenceService = inject(UserGeofenceService);
  private readonly injector = inject(Injector);
  private readonly snackBar = inject(MatSnackBar);
  /** The profile's areas as read here, for the inherited option's summary. */
  private readonly subscribedAreas = signal<string[]>([]);

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

  /** Null while the field is empty: a number input bound with ngModel reports an empty value as null. */
  readonly distanceKm = signal<null | number>(1);

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

  /**
   * Draws the scope on a small map under the options. On in the alarm dialogs' Delivery tab; the card's
   * where sheet turns it off to stay a short sheet.
   */
  readonly preview = input(true);

  /**
   * Areas the profile subscribes to, so the inherited option can say what it means. A host that has
   * them passes them; otherwise the picker uses the list it reads for its own area options, which is
   * the same list. Only the card's sheet used to pass them, so every dialog said "anywhere".
   */
  readonly profileAreas = input<string[]>([]);

  /** The profile's areas as stored, from the host when it passed them and from the picker's own read otherwise. */
  readonly profileAreaNames = computed(() => {
    const passed = this.profileAreas();
    return passed.length > 0 ? passed : this.subscribedAreas();
  });

  readonly profileAreaSummary = computed(() =>
    this.profileAreaNames()
      .map(area => this.areaNames.label(area))
      .join(', '),
  );

  /**
   * The radius the rule was stored with, when editing one. Production holds rules past 100 km, and an
   * edit that leaves such a radius alone has to save; only a changed radius is held to the range.
   */
  readonly storedRadiusKm = input<null | number>(null);

  /**
   * True when the radius field holds something the dialog won't save. Only "near" measures by it; the
   * other two options keep whatever the field last held and never send it.
   */
  readonly radiusError = computed(() => this.mode() === 'near' && !radiusAcceptable(this.distanceKm(), this.storedRadiusKm()));
  /** Shows the field's error from the same rule that turns Save off, rather than from the browser's min/max. */
  protected readonly radiusErrorMatcher: ErrorStateMatcher = { isErrorState: () => this.radiusError() };

  protected readonly radiusMax = MAX_RADIUS_KM;

  protected readonly radiusMin = MIN_RADIUS_KM;

  /**
   * Two-way so the host can keep Save off while the radius is out of range. It is derived from the
   * scope the host already holds plus the stored radius, so it comes out the same when a tab switch
   * destroys and rebuilds the picker.
   */
  readonly radiusValid = model(true);

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
    // `??`, not `||`: a stored 0 (an emptied field, or a place rule with no radius) stays 0 and is refused,
    // where `||` quietly made it 1 km.
    this.distanceKm.set(initial.distanceKm ?? 1);
    this.selectedAreas.set(initial.areas ?? []);

    // Only mirror outward after seeding, or the write-back races the seed.
    effect(() => this.scope.set(this.currentScope()), { injector: this.injector });
    effect(() => this.radiusValid.set(!this.radiusError()), { injector: this.injector });

    this.places.load().subscribe({ error: () => undefined });

    // The instance's full area list, narrowed to the ones this user actually subscribes to. This
    // control sits under "Anywhere in my areas" and reads "Only in specific areas" -- it narrows where
    // a rule reaches within the areas you already get alerts for, so offering the whole server turns it
    // into a second, hidden way to subscribe. On a multi-community instance that is hundreds of areas
    // from cities the reader has nothing to do with. See #873.
    //
    // Anything the rule ALREADY carries stays on offer even when it is no longer subscribed, or the
    // option backing it disappears and the area drops off the rule silently on the next save. One rule
    // in production is in exactly that state.
    // Each half fails on its own, so a failed instance list can't take the profile summary with it.
    forkJoin({
      available: this.areaService.getAvailable().pipe(catchError(() => of([]))),
      selected: this.areaService.getSelected().pipe(catchError(() => of<string[]>([]))),
    }).subscribe({
      next: ({ available, selected }) => {
        this.subscribedAreas.set(selected);
        const keep = new Set([...selected, ...(this.scope().areas ?? [])].map(name => name.toLowerCase()));
        const mine = available.filter(area => keep.has(area.name.toLowerCase()));

        this.availableAreas.update(current => [...mine.map(a => ({ name: a.name, own: false })), ...current]);
      },
    });

    this.geofenceService.getCustomGeofences().subscribe({
      error: () => undefined,
      next: own => {
        this.areaNames.learn(own);
        this.availableAreas.update(current => [...current, ...own.map(g => ({ name: g.kojiName, own: true }))]);
      },
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

  /** The bounds in the display language's number format ("0,1" in German). */
  protected radiusRange(): { max: string; min: string } {
    const lang = this.translate.getCurrentLang() ?? 'en';
    return { max: formatKm(MAX_RADIUS_KM, lang), min: formatKm(MIN_RADIUS_KM, lang) };
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
    return this.areaNames.label(area);
  }

  private currentScope(): AlarmScope {
    switch (this.mode()) {
      case 'areas':
        return { areas: this.selectedAreas(), mode: 'areas' };
      case 'near':
        return this.placeLabel()
          ? { distanceKm: this.distanceKm() ?? 0, mode: 'place', placeLabel: this.placeLabel() }
          : { distanceKm: this.distanceKm() ?? 0, mode: 'profile' };
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
            ...PLACE_NAME_PROMPT,
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
          // Swallowing this left the alarm aimed at nothing with no word about why.
          error: err =>
            this.snackBar.open(serverReason(err) ?? this.translate.instant('WHERE.PLACE_SAVE_ERROR'), this.translate.instant('COMMON.OK'), {
              duration: 6000,
            }),
          // Select it straight away: the only reason to make a place here is to use it here.
          next: () => this.placeLabel.set(label),
        });
      });
  }
}

/**
 * A stored scope back into the picker's three options. A profile scope carrying a radius, any radius,
 * lands on "near": scopeOf leaves distanceKm out when there is none, so 0 here is an emptied field that
 * must come back refused rather than as "anywhere in my areas".
 */
function initialMode(scope: AlarmScope): PickerMode {
  if (scope.mode === 'areas') return 'areas';
  if (scope.mode === 'place') return 'near';
  return scope.distanceKm === undefined ? 'inherit' : 'near';
}
