import { NgTemplateOutlet, SlicePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, OnInit, DestroyRef, inject, signal, computed, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { finalize, switchMap } from 'rxjs';

import { AreaDefinition, GeofenceData, Location } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { I18nService } from '../../core/services/i18n.service';
import { LocationService } from '../../core/services/location.service';
import { PlacesService } from '../../core/services/places.service';
import { SettingsService } from '../../core/services/settings.service';
import { AreaMapComponent, MapPlace } from '../../shared/components/area-map/area-map.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { LocationDialogComponent } from '../../shared/components/location-dialog/location-dialog.component';
import { PlacesSectionComponent } from '../../shared/components/places-section/places-section.component';
import { QuietChipComponent } from '../../shared/components/quiet-chip/quiet-chip.component';
import { RegionOption, RegionSelectorComponent } from '../../shared/components/region-selector/region-selector.component';
import { TranslatePluralPipe } from '../../shared/pipes/translate-plural.pipe';
import { hasPin, pinOrNull } from '../../shared/utils/location.utils';
import { serverReason } from '../../shared/utils/server-reason';

interface AreaItem {
  group: string;
  name: string;
  selected: boolean;
}

/**
 * PoracleNG's comparison key for an area name (`NormalizeAreaKey`): lowercase, trimmed, `_` equal to a space.
 * Used to tell which of the names sent it kept.
 */
function areaKey(name: string): string {
  return name.trim().toLowerCase().replace(/_/g, ' ');
}

interface GroupInfo {
  name: string;
  selectedCount: number;
  totalCount: number;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    QuietChipComponent,
    NgTemplateOutlet,
    SlicePipe,
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatChipsModule,
    MatDialogModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    TranslatePipe,
    TranslatePluralPipe,
    AreaMapComponent,
    RegionSelectorComponent,
    PlacesSectionComponent,
  ],
  selector: 'app-area-list',
  standalone: true,
  styleUrl: './area-list.component.scss',
  templateUrl: './area-list.component.html',
})
export class AreaListComponent implements OnInit {
  readonly availableAreas = signal<AreaDefinition[]>([]);
  /**
   * Lowercased stored name to the name Poracle actually publishes.
   *
   * Subscriptions are stored lowercased in `humans.area` and `profiles.area` because Poracle matches
   * area names case-sensitively and the lowercase form is what matches. That is the storage contract,
   * not a display decision, and rendering it verbatim is why the chips and the quiet dialog read
   * "mechanicsville". Of 934 fences on one instance, 883 carry capitals.
   */
  private readonly areaNamesByLower = computed(() => new Map(this.availableAreas().map(area => [areaKey(area.name), area.name])));

  private readonly areaService = inject(AreaService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);
  private readonly locationService = inject(LocationService);
  private readonly rawGeofenceData = signal<GeofenceData[]>([]);

  /** The caller's own drawn fences, by stored key, to the name they gave them. They're not in `availableAreas`. */
  private readonly ownFenceNames = computed(
    () => new Map(this.rawGeofenceData().flatMap(g => (g.displayName ? [[areaKey(g.name), g.displayName] as const] : []))),
  );

  private readonly pinPanel = viewChild('pinPanel', { read: ElementRef });

  private pinPanelScroll?: ReturnType<typeof setTimeout>;
  /**
   * What PoracleNG holds for the active profile, as last read or saved. The Areas card and the quiet chips
   * speak about this, not about the ticks: an area ticked and not yet saved is not active, and has no
   * alerts to quiet.
   */
  readonly savedAreas = signal<string[]>([]);
  private readonly savedAreaKeys = computed(() => new Set(this.savedAreas().map(a => areaKey(a))));

  private readonly settingsService = inject(SettingsService);

  private readonly snackBar = inject(MatSnackBar);

  /** Label for areas Koji reports with no group. Doubles as the key the group filter matches on. */
  private readonly ungroupedLabel = this.i18n.instant('AREAS.GROUP_UNGROUPED');

  readonly activeGroup = signal<string | null>(null);

  readonly areas = signal<AreaItem[]>([]);

  readonly allGroups = computed((): GroupInfo[] => {
    const all = this.areas();
    const groupMap = new Map<string, { selected: number; total: number }>();
    for (const area of all) {
      const key = area.group || this.ungroupedLabel;
      if (!groupMap.has(key)) groupMap.set(key, { selected: 0, total: 0 });
      const g = groupMap.get(key)!;
      g.total++;
      if (area.selected) g.selected++;
    }
    return [...groupMap.entries()]
      .map(([name, counts]) => ({ name, selectedCount: counts.selected, totalCount: counts.total }))
      .sort((a, b) => a.name.localeCompare(b.name));
  });

  /** True from the Clear click until its confirm closes, so a double click opens one dialog. */
  readonly clearingPin = signal(false);

  /** Street addresses for the pin. Off with `disable_nominatim`, and with location itself. */
  readonly geocodingEnabled = computed(
    () => !this.settingsService.isDisabled('disable_location') && !this.settingsService.isDisabled('disable_nominatim'),
  );

  readonly geofenceData = computed(() => {
    const available = this.availableAreas();
    const raw = this.rawGeofenceData();
    if (available.length === 0) return [];
    const accessibleNames = new Set(available.map(a => a.name));
    return raw.filter(g => accessibleNames.has(g.name));
  });

  readonly groupMapping = computed(() => {
    const map = new Map<string, string>();
    for (const area of this.availableAreas()) {
      map.set(area.name, area.group ?? '');
    }
    return map;
  });

  readonly groupOptions = computed((): RegionOption[] => {
    return this.allGroups().map(g => ({
      label: g.name,
      selectedCount: g.selectedCount,
      totalCount: g.totalCount,
      ungrouped: g.name === this.ungroupedLabel,
    }));
  });

  readonly selectedAreas = signal<string[]>([]);

  readonly hasChanges = computed(() => {
    // Compared by PoracleNG's key, so `west_end` held and `West End` ticked are one area, not a change.
    const current = this.selectedAreas().map(areaKey).sort();
    const saved = this.savedAreas().map(areaKey).sort();
    if (current.length !== saved.length) return true;
    return current.some((v, i) => v !== saved[i]);
  });

  readonly hasMultipleGroups = computed(() => {
    const groups = new Set(this.areas().map(a => a.group));
    return groups.size > 1;
  });

  readonly loading = signal(true);

  readonly location = signal<Location | null>(null);

  readonly locationAddress = signal<string>('');

  /**
   * The pin card and the Places section. `disable_location` 403s every route behind both, so with it on
   * they are simply absent: the card used to say "No pin set" to a user who has one, and offer a Set
   * button whose save would be refused. The areas half of the page is a separate feature.
   */
  readonly locationEnabled = computed(() => !this.settingsService.isDisabled('disable_location'));

  readonly locationMapUrl = signal<string>('');

  manualAreaName = '';
  readonly places = inject(PlacesService);
  /** The named places, drawn on the map beside the pin. None while location is switched off. */
  readonly mapPlaces = computed((): MapPlace[] => (this.locationEnabled() ? this.places.named() : []));
  /** The pin and places panel. Closed by default: the map above it already shows both. */
  readonly pinPanelOpen = signal(false);
  readonly saving = signal(false);

  searchText = '';

  /** True when the saved selection could not be read, which makes saving unsafe. See #643. */
  readonly selectionUnknown = signal(false);

  readonly userLocationForMap = computed(() => {
    const loc = this.location();
    if (loc && loc.latitude && loc.longitude) {
      return { lat: loc.latitude, lng: loc.longitude };
    }
    return undefined;
  });

  readonly viewMode = signal<'map' | 'list'>('map');

  readonly visibleAreas = computed(() => {
    const search = this.searchText.toLowerCase();
    const group = this.activeGroup();
    return this.areas().filter(a => {
      if (search && !a.name.toLowerCase().includes(search)) return false;
      if (group && (a.group || this.ungroupedLabel) !== group) return false;
      return true;
    });
  });

  addManualArea(): void {
    const name = this.manualAreaName.trim();
    if (!name) return;
    if (!this.selectedAreas().includes(name)) {
      this.selectedAreas.set([...this.selectedAreas(), name]);
    }
    this.manualAreaName = '';
  }

  applyFilter(): void {
    // Triggers visibleAreas recomputation via searchText binding
  }

  /**
   * The name to show for a selected area. Falls back to the stored string, which is what a
   * subscription to an area since deleted resolves to -- it has no definition to look up, and it has
   * to keep rendering so the user can see it and remove it.
   */
  areaDisplayName(area: string): string {
    return this.areaNamesByLower().get(areaKey(area)) ?? this.ownFenceNames().get(areaKey(area)) ?? area;
  }

  cancelChanges(): void {
    const savedSet = this.savedAreaKeys();
    for (const a of this.areas()) {
      a.selected = savedSet.has(areaKey(a.name));
    }
    this.selectedAreas.set([...this.savedAreas()]);
  }

  clearAllAreas(): void {
    for (const a of this.areas()) a.selected = false;
    this.syncSelectedFromAreas();
  }

  /**
   * Asks before clearing, naming how many alarms measure their radius from the pin: clearing stops every
   * one of them matching, and there is no undo. A count that cannot be read still asks, without a number.
   */
  clearLocation(): void {
    if (this.clearingPin()) return;
    this.clearingPin.set(true);
    this.locationService
      .getPinUsage()
      .pipe(
        switchMap(count =>
          this.dialog
            .open(ConfirmDialogComponent, {
              data: {
                confirmText: this.i18n.instant('AREAS.CLEAR_LOCATION'),
                message:
                  count === null
                    ? this.i18n.instant('AREAS.CLEAR_PIN_CONFIRM_UNKNOWN')
                    : count === 0
                      ? this.i18n.instant('AREAS.CLEAR_PIN_CONFIRM_NONE')
                      : this.i18n.plural('AREAS.CLEAR_PIN_CONFIRM', count),
                title: this.i18n.instant('AREAS.CLEAR_PIN_TITLE'),
                warn: true,
              } satisfies ConfirmDialogData,
            })
            .afterClosed(),
        ),
        finalize(() => this.clearingPin.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(confirmed => {
        if (confirmed) this.clearPin();
      });
  }

  clearSearch(): void {
    this.searchText = '';
  }

  deselectAllVisible(): void {
    for (const a of this.visibleAreas()) a.selected = false;
    this.syncSelectedFromAreas();
  }

  /** Whether the area is subscribed on the server, rather than only ticked on this page. */
  isSaved(name: string): boolean {
    return this.savedAreaKeys().has(areaKey(name));
  }

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => clearTimeout(this.pinPanelScroll));
    this.loadData();
  }

  onGroupFilterSelected(option: RegionOption): void {
    this.activeGroup.set(option.label || null);
  }

  onMapAreaClicked(name: string): void {
    const lowerName = areaKey(name);
    const area = this.areas().find(a => areaKey(a.name) === lowerName);
    if (area) {
      this.toggleAreaDirect(area);
    }
  }

  openLocationDialog(): void {
    const ref = this.dialog.open(LocationDialogComponent, {
      data: this.location(),
    });
    ref.afterClosed().subscribe((result: Location | undefined) => {
      if (result) {
        this.location.set(result);
        this.locationAddress.set('');
        this.locationMapUrl.set('');
        if (hasPin(result)) {
          this.lookUpAddress(result);
          this.locationService
            .getStaticMapUrl(result.latitude, result.longitude)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe(map => {
              if (map?.url) this.locationMapUrl.set(map.url);
            });
        }
      }
    });
  }

  /** A marker was clicked: open the panel that manages the pin and places, and bring it into view. */
  openPinPanel(): void {
    this.pinPanelOpen.set(true);
    // After the panel has had a frame to start opening, so the scroll lands on its expanded position.
    clearTimeout(this.pinPanelScroll);
    this.pinPanelScroll = setTimeout(() => this.pinPanel()?.nativeElement.scrollIntoView?.({ behavior: 'smooth', block: 'nearest' }));
  }

  removeAreaDirect(name: string): void {
    const lowerName = areaKey(name);
    const area = this.areas().find(a => areaKey(a.name) === lowerName);
    if (area) {
      area.selected = false;
      this.syncSelectedFromAreas();
    } else {
      // Custom geofence not in available areas list — remove directly
      this.selectedAreas.update(selected => selected.filter(a => areaKey(a) !== lowerName));
    }
  }

  saveAreas(): void {
    this.saving.set(true);
    const areaListNames = new Set(this.areas().map(a => areaKey(a.name)));
    const fromChecklist = this.areas()
      .filter(a => a.selected)
      .map(a => a.name);
    // Preserve custom geofence names that aren't in the available areas list
    const customNames = this.selectedAreas().filter(a => !areaListNames.has(areaKey(a)));
    const selected = [...fromChecklist, ...customNames];

    this.areaService
      .update(selected)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err => {
          this.saving.set(false);
          const message = this.saveRefusal(err);
          this.snackBar.open(message ?? this.i18n.instant('AREAS.SNACK_FAILED'), this.i18n.instant('TOAST.OK'), {
            duration: message ? 6000 : 3000,
          });
        },
        next: stored => this.applySaved(selected, stored),
      });
  }

  selectAllVisible(): void {
    for (const a of this.visibleAreas()) a.selected = true;
    this.syncSelectedFromAreas();
  }

  toggleAreaDirect(area: AreaItem): void {
    area.selected = !area.selected;
    this.syncSelectedFromAreas();
  }

  /**
   * The page takes what PoracleNG kept, not what it was sent. PUT /api/areas answers with the profile's list
   * read back after the write, and a name PoracleNG has no fence for (a renamed or deleted area) is dropped
   * there without an error. Trusting the names sent showed such an area as active, with a quiet chip, until
   * the next reload; now it is unticked and the snackbar names it.
   */
  private applySaved(sent: string[], stored: null | string[] | undefined): void {
    const kept = Array.isArray(stored) ? stored : sent;
    const keptKeys = new Set(kept.map(areaKey));
    const dropped = sent.filter(name => !keptKeys.has(areaKey(name)));

    for (const a of this.areas()) a.selected = keptKeys.has(areaKey(a.name));
    this.savedAreas.set([...kept]);
    this.selectedAreas.set([...kept]);
    this.saving.set(false);

    if (dropped.length > 0) {
      this.snackBar.open(
        this.i18n.instant('AREAS.SNACK_SAVED_DROPPED', { names: dropped.map(name => this.areaDisplayName(name)).join(', ') }),
        this.i18n.instant('TOAST.OK'),
        { duration: 6000 },
      );
    } else {
      this.snackBar.open(this.i18n.instant('AREAS.SNACK_UPDATED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
    }
  }

  private buildAreaList(): void {
    // DB stores lowercase area names, API may return mixed case
    const selectedSet = new Set(this.selectedAreas().map(a => areaKey(a)));
    const available = this.availableAreas();
    this.areas.set(
      available
        .map(a => ({
          name: a.name,
          group: a.group ?? '',
          selected: selectedSet.has(areaKey(a.name)),
        }))
        .sort((a, b) => a.name.localeCompare(b.name)),
    );
  }

  private clearPin(): void {
    this.locationService
      .setLocation({ latitude: 0, longitude: 0 })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err =>
          this.snackBar.open(serverReason(err) ?? this.i18n.instant('AREAS.SNACK_LOCATION_FAILED'), this.i18n.instant('TOAST.OK'), {
            duration: 4000,
          }),
        next: () => {
          this.location.set(null);
          this.locationAddress.set('');
          this.locationMapUrl.set('');
          this.snackBar.open(this.i18n.instant('AREAS.SNACK_LOCATION_CLEARED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
      });
  }

  private loadData(): void {
    this.loading.set(true);
    let loaded = 0;
    const check = () => {
      loaded++;
      if (loaded >= 3) {
        this.buildAreaList();
        this.loading.set(false);
      }
    };

    this.areaService
      .getSelected()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        // A page that could not read the current subscriptions must not offer to overwrite them. This
        // arm used to advance the counter and nothing else, so a single failed GET /api/areas rendered
        // every checkbox unticked and "0 areas selected" with no error -- and saving then posted only
        // whatever the user ticked next, destroying every other subscription they had. See #643.
        error: () => {
          this.selectionUnknown.set(true);
          this.snackBar.open(this.i18n.instant('AREAS.SNACK_LOAD_SELECTED_FAILED'), this.i18n.instant('TOAST.OK'), {
            duration: 6000,
          });
          check();
        },
        next: areas => {
          this.selectionUnknown.set(false);
          this.savedAreas.set([...areas]);
          this.selectedAreas.set(areas);
          check();
        },
      });

    this.areaService
      .getAvailable()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => check(),
        next: areas => {
          this.availableAreas.set(areas.filter(a => a.userSelectable !== false));
          check();
        },
      });

    if (!this.locationEnabled()) {
      // The pin is not this page's to show, so there is nothing to wait for.
      check();
    } else {
      this.loadLocation(check);
    }

    this.areaService
      .getGeofencePolygons()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {},
        next: data => this.rawGeofenceData.set(data),
      });
  }

  private loadLocation(check: () => void): void {
    this.locationService
      .getLocation()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => check(),
        next: loc => {
          // 0,0 is a cleared pin, not a place in the Atlantic. See pinOrNull.
          this.location.set(pinOrNull(loc));
          check();
          if (hasPin(loc)) {
            this.lookUpAddress(loc);
            this.locationService
              .getStaticMapUrl(loc.latitude, loc.longitude)
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe(result => {
                if (result?.url) this.locationMapUrl.set(result.url);
              });
          }
        },
      });
  }

  /** The address line under the pin. Skipped with geocoding off; the card falls back to coordinates. */
  private lookUpAddress(loc: Location): void {
    if (!this.geocodingEnabled()) return;
    this.locationService
      .reverseGeocode(loc.latitude, loc.longitude)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(result => {
        if (result?.displayName) this.locationAddress.set(result.displayName);
      });
  }

  /**
   * Why a save was refused, when the server said. PUT /api/areas answers 400 `{ error, areas }` naming each
   * area this user may not select, and saves nothing; the names are shown as published, in the user's
   * language.
   */
  private saveRefusal(err: unknown): null | string {
    const refused = (err as { error?: { areas?: unknown } } | null)?.error?.areas;
    if (Array.isArray(refused) && refused.length > 0) {
      const names = refused.filter((a): a is string => typeof a === 'string').map(a => this.areaDisplayName(a));
      return this.i18n.instant('AREAS.SNACK_NOT_PERMITTED', { names: names.join(', ') });
    }
    return serverReason(err);
  }

  private syncSelectedFromAreas(): void {
    const areaListNames = new Set(this.areas().map(a => areaKey(a.name)));
    const fromChecklist = this.areas()
      .filter(a => a.selected)
      .map(a => a.name);
    // Preserve custom geofence names that aren't in the predefined areas list
    const customNames = this.selectedAreas().filter(a => !areaListNames.has(areaKey(a)));
    this.selectedAreas.set([...fromChecklist, ...customNames]);
  }
}
