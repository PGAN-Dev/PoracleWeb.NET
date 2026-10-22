import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, effect, inject, signal, computed, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterModule } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { switchMap, forkJoin, EMPTY, catchError } from 'rxjs';

import { DashboardCounts, GeofenceData, Location, Profile, WeatherData } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { AuthService } from '../../core/services/auth.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { I18nService } from '../../core/services/i18n.service';
import { LocationService } from '../../core/services/location.service';
import { MuteService } from '../../core/services/mute.service';
import { ProfileService } from '../../core/services/profile.service';
import { SettingsService } from '../../core/services/settings.service';
import { AreaOverviewMapComponent } from '../../shared/components/area-overview-map/area-overview-map.component';
import { LocationDialogComponent } from '../../shared/components/location-dialog/location-dialog.component';
import { OnboardingComponent } from '../../shared/components/onboarding/onboarding.component';
import { QuietListSheetComponent } from '../../shared/components/quiet-list-sheet/quiet-list-sheet.component';
import { TranslatePluralPipe } from '../../shared/pipes/translate-plural.pipe';
import { ADD_ON_ARRIVAL } from '../../shared/utils/add-on-arrival';
import { polygonCentroid } from '../../shared/utils/geo.utils';
import { RuleEdge, typeEdge } from '../../shared/utils/rule-colours';
import { serverReason } from '../../shared/utils/server-reason';

interface DashboardCard {
  /** The `disable_*` key that governs this alarm type. Eggs share the raid one. */
  disableKey: string;
  icon: string;
  key: keyof DashboardCounts;
  label: string;
  route: string;
}

/** How many zero-count types the "Not tracking" line names before folding the rest into "+N". */
const IDLE_PREVIEW = 3;

/** How many area names the strip spells out before "+N". */
const AREA_PREVIEW = 2;

interface Tip {
  action: string;
  icon: string;
  id: string;
  message: string;
  queryParams?: Record<string, number>;
  route: string | null;
  type: 'warning' | 'info';
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
    MatSnackBarModule,
    MatTooltipModule,
    AreaOverviewMapComponent,
    OnboardingComponent,
    RouterModule,
    TranslatePipe,
    TranslatePluralPipe,
  ],
  selector: 'app-dashboard',
  standalone: true,
  styleUrl: './dashboard.component.scss',
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent implements OnInit {
  readonly geofencePolygons = signal<GeofenceData[]>([]);
  /**
   * Stored area names are lowercase, because Poracle matches case-sensitively; the fence keeps its own
   * casing, which is what the Areas page shows. Display only: mutes and links keep the stored name.
   */
  private readonly areaNamesByLower = computed(() => new Map(this.geofencePolygons().map(g => [g.name.toLowerCase(), g.name])));
  private readonly areaService = inject(AreaService);
  private readonly authService = inject(AuthService);
  private readonly dashboardService = inject(DashboardService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);
  private readonly locationService = inject(LocationService);
  private readonly profileService = inject(ProfileService);
  private readonly snackBar = inject(MatSnackBar);

  readonly profileNo = computed(() => this.authService.user()?.profileNo ?? 1);

  readonly profiles = signal<Profile[]>([]);

  readonly profileName = computed(() => {
    const profiles = this.profiles();
    if (profiles.length === 0) return this.i18n.instant('DASHBOARD.DEFAULT_PROFILE');
    const no = this.profileNo();
    const match = profiles.find(p => p.profileNo === no);
    return match?.name ?? this.i18n.instant('DASHBOARD.DEFAULT_PROFILE');
  });

  /**
   * "Your active profile is now X". It used to run once in ngOnInit and read the name from a profile list
   * that had not loaded yet, so it named the profile the user had just been moved off. /api/auth/me
   * carries the new profile's name with the refreshed token, and may land after the dashboard opens, so
   * this waits for it. The cause is not stated: the scheduler, the bot's !profile and another tab all
   * look the same from here.
   */
  private readonly resyncNotice = effect(() => {
    if (!this.authService.profileResynced()) return;
    const user = this.authService.user();
    untracked(() => {
      const name = user?.profileName || this.profileName();
      this.snackBar.open(this.i18n.instant('DASHBOARD.PROFILE_RESYNCED', { name }), this.i18n.instant('TOAST.OK'), { duration: 5000 });
      this.authService.clearProfileResynced();
    });
  });

  private readonly router = inject(Router);

  private readonly settingsService = inject(SettingsService);

  readonly counts = signal<DashboardCounts | null>(null);

  readonly cards: DashboardCard[] = [
    {
      disableKey: 'disable_mons',
      icon: 'catching_pokemon',
      key: 'pokemon',
      label: 'DASHBOARD.CARD_POKEMON',
      route: '/pokemon',
    },
    {
      disableKey: 'disable_raids',
      icon: 'shield',
      key: 'raids',
      label: 'DASHBOARD.CARD_RAIDS',
      route: '/raids',
    },
    {
      disableKey: 'disable_raids',
      icon: 'egg',
      key: 'eggs',
      label: 'DASHBOARD.CARD_EGGS',
      route: '/raids',
    },
    {
      disableKey: 'disable_quests',
      icon: 'explore',
      key: 'quests',
      label: 'DASHBOARD.CARD_QUESTS',
      route: '/quests',
    },
    {
      disableKey: 'disable_invasions',
      icon: 'warning',
      key: 'invasions',
      label: 'DASHBOARD.CARD_INVASIONS',
      route: '/invasions',
    },
    {
      disableKey: 'disable_showcase',
      icon: 'celebration',
      key: 'pokestopEvents',
      label: 'DASHBOARD.CARD_POKESTOP_EVENTS',
      route: '/pokestop-events',
    },
    {
      disableKey: 'disable_lures',
      icon: 'location_on',
      key: 'lures',
      label: 'DASHBOARD.CARD_LURES',
      route: '/lures',
    },
    {
      disableKey: 'disable_nests',
      icon: 'park',
      key: 'nests',
      label: 'DASHBOARD.CARD_NESTS',
      route: '/nests',
    },
    {
      disableKey: 'disable_gyms',
      icon: 'fitness_center',
      key: 'gyms',
      label: 'DASHBOARD.CARD_GYMS',
      route: '/gyms',
    },
    {
      disableKey: 'disable_fort_changes',
      icon: 'domain',
      key: 'fortChanges',
      label: 'DASHBOARD.CARD_FORT_CHANGES',
      route: '/fort-changes',
    },
    {
      disableKey: 'disable_maxbattles',
      icon: 'flash_on',
      key: 'maxBattles',
      label: 'DASHBOARD.CARD_MAX_BATTLES',
      route: '/max-battles',
    },
  ];

  /**
   * Cards for the alarm types this instance actually offers. A disabled type is gone from here as it
   * is from the sidebar, the route and the API — a card linking to a page that answers 403 would be
   * worse than no card. Rules already stored on a disabled type stay dormant and come back intact if
   * it is switched on again. See #792.
   */
  readonly visibleCards = computed(() => this.cards.filter(card => !this.settingsService.isDisabled(card.disableKey)));

  /**
   * The types that have alarms, in sidebar order, each with its count. These are the inventory the
   * user came to check; the zeros that used to sit beside them as greyed tiles are on one line below.
   */
  readonly activeTypes = computed(() => {
    const c = this.counts();
    if (!c) return [];
    return this.visibleCards()
      .filter(card => (c[card.key] ?? 0) > 0)
      .map(card => ({ card, count: c[card.key] }));
  });

  readonly alertsPaused = computed(() => {
    const user = this.authService.user();
    return user ? !user.enabled : false;
  });

  readonly selectedAreas = signal<string[]>([]);

  readonly areaMore = computed(() => Math.max(0, this.selectedAreas().length - AREA_PREVIEW));
  /** "aberdeen, toledo" and how many more: the strip names areas, the chips under the map list them all. */
  readonly areaPreview = computed(() =>
    this.selectedAreas()
      .slice(0, AREA_PREVIEW)
      .map(area => this.areaLabel(area))
      .join(', '),
  );

  // The dashboard used to load every one of these regardless, so with any of the three switches on it
  // opened with an error toast for a feature the user had not touched -- and kept rendering buttons that
  // bounced straight back with the same toast. A disabled feature is simply absent here. See #516.
  readonly areasEnabled = computed(() => !this.settingsService.isDisabled('disable_areas'));
  readonly areaWeather = signal<Record<string, WeatherData>>({});

  readonly dismissedTips = signal<string[]>(JSON.parse(sessionStorage.getItem('dismissed-tips') || '[]'));

  readonly locationEnabled = computed(() => !this.settingsService.isDisabled('disable_location'));

  /** Street addresses for the pin. `disable_nominatim` 403s the lookup, and so does `disable_location`. */
  readonly geocodingEnabled = computed(() => this.locationEnabled() && !this.settingsService.isDisabled('disable_nominatim'));

  /** Enabled types with nothing tracked. Empty until the counts arrive. */
  readonly idleTypes = computed(() => {
    const c = this.counts();
    if (!c) return [];
    return this.visibleCards().filter(card => (c[card.key] ?? 0) === 0);
  });

  /** The "Not tracking" line starts folded; "+N" opens it. */
  readonly showAllIdle = signal(false);

  readonly idlePreview = computed(() => (this.showAllIdle() ? this.idleTypes() : this.idleTypes().slice(0, IDLE_PREVIEW)));
  readonly idleHidden = computed(() => this.idleTypes().length - this.idlePreview().length);

  /**
   * "Eggs, Invasions, Lures": the folded line's names, in the display language. `instant` is not a
   * signal, so the language is read here to recompute on a switch rather than when the counts next change.
   */
  readonly idleNames = computed(() => {
    this.i18n.currentLang();
    return this.idlePreview()
      .map(card => this.i18n.instant(card.label))
      .join(', ');
  });

  /** Read by the template to decide whether there is anything to unfold. */
  readonly idlePreviewSize = IDLE_PREVIEW;

  readonly location = signal<Location | null>(null);

  readonly locationAddress = signal<string>('');

  readonly locationMapUrl = signal<string>('');

  protected readonly mutes = inject(MuteService);

  /**
   * The one primary action: a menu of the alarm types this instance offers, each opening its list with
   * the add dialog up (`?add=1`). Eggs are added from the raid dialog, so they are not a second entry.
   * Gated on the same keys as the sidebar, so a switched-off type is not offered.
   */
  readonly newAlarmTypes = computed(() => this.visibleCards().filter(card => card.key !== 'eggs'));

  readonly profilesEnabled = computed(() => !this.settingsService.isDisabled('disable_profiles'));

  /**
   * "3 quiet, next back in 47m". A count on its own says nothing about when it ends, and the soonest
   * expiry is the one the user is waiting on.
   */
  protected readonly quietSummary = computed(() => {
    this.i18n.currentLang(); // recompute on a language switch; instant() is not a signal
    const count = this.mutes.mutes().length;
    const soonest = this.mutes.soonestExpiry();
    const remaining = soonest === null ? 0 : Math.max(0, soonest - Math.floor(Date.now() / 1000));

    return this.i18n.instant('QUIET.CARD_SUMMARY', {
      count,
      countdown: this.mutes.countdownLabel(remaining),
    });
  });

  readonly showOnboarding = signal(!localStorage.getItem('poracle-onboarding-complete'));

  readonly userLocation = computed(() => {
    const loc = this.location();
    if (!loc) return false;
    return loc.latitude !== 0 || loc.longitude !== 0;
  });

  readonly weather = signal<WeatherData | null>(null);

  /**
   * The weather card is drawn only once there is weather. With none for the pin it read "No data" and
   * took a quarter of the status row; a loading skeleton that then vanished made the row jump.
   */
  readonly showWeather = computed(() => this.userLocation() && this.weather() !== null);

  readonly skeletonItems = [1, 2, 3];

  readonly tips = computed(() => {
    this.i18n.currentLang(); // recompute on a language switch; instant() is not a signal
    const tips: Tip[] = [];

    // Each tip leads somewhere, so each follows the switch for where it leads. A tip for a disabled
    // feature was a link to a route guard.
    if (this.locationEnabled() && !this.userLocation()) {
      tips.push({
        id: 'no-location',
        action: this.i18n.instant('DASHBOARD.TIP_NO_LOCATION_ACTION'),
        icon: 'location_off',
        message: this.i18n.instant('DASHBOARD.TIP_NO_LOCATION'),
        route: null,
        type: 'warning',
      });
    }

    if (this.areasEnabled() && this.selectedAreas().length === 0) {
      tips.push({
        id: 'no-areas',
        action: this.i18n.instant('DASHBOARD.TIP_NO_AREAS_ACTION'),
        icon: 'map',
        message: this.i18n.instant('DASHBOARD.TIP_NO_AREAS'),
        route: '/areas',
        type: 'info',
      });
    }

    const c = this.counts();
    // The tip exists to get a first alarm made, so it points at the first type this instance offers.
    const firstType = this.visibleCards()[0];
    if (c && firstType && Object.values(c).every(v => v === 0)) {
      tips.push({
        id: 'no-alarms',
        action: this.i18n.instant('DASHBOARD.TIP_NO_ALARMS_ACTION'),
        icon: 'add_alert',
        message: this.i18n.instant('DASHBOARD.TIP_NO_ALARMS'),
        queryParams: ADD_ON_ARRIVAL,
        route: firstType.route,
        type: 'info',
      });
    }

    return tips.filter(t => !this.dismissedTips().includes(t.id));
  });

  /** The total of what the row shows: a switched-off type's stored alarms are dormant and not listed. */
  readonly totalAlarms = computed(() => this.activeTypes().reduce((sum, t) => sum + t.count, 0));

  readonly username = computed(() => this.authService.user()?.username ?? 'Trainer');

  readonly weatherUpdatedAgo = computed(() => {
    const w = this.weather();
    if (!w?.updatedAt) return '';
    const diff = Math.floor((Date.now() - new Date(w.updatedAt).getTime()) / 60000);
    if (diff < 1) return this.i18n.instant('DASHBOARD.WEATHER_JUST_NOW');
    if (diff === 1) return this.i18n.instant('DASHBOARD.WEATHER_1_MIN_AGO');
    if (diff < 60) return this.i18n.instant('DASHBOARD.WEATHER_MIN_AGO', { count: diff });
    if (diff < 120) return this.i18n.instant('DASHBOARD.WEATHER_1_HR_AGO');
    return this.i18n.plural('DASHBOARD.WEATHER_HRS_AGO', Math.floor(diff / 60));
  });

  /**
   * How long an area on the chips has left of its quiet period, or null. Matched case-insensitively:
   * PoracleNG hands back the fence's own casing and area names are stored lowercase here.
   */
  areaLabel(area: string): string {
    return this.areaNamesByLower().get(area.toLowerCase()) ?? area;
  }

  areaQuietCountdown(area: string): null | string {
    const remaining = this.mutes.remainingFor('area', area);
    return remaining === null ? null : this.mutes.countdownLabel(remaining);
  }

  /** A boosted type's colour, from the same table the Pokémon cards use, as a light and a dark value. */
  boostedTypeEdge(type: string): RuleEdge {
    return typeEdge(type) ?? { dark: '#9e9e9e', light: '#757575' };
  }

  dismissTip(tip: Tip): void {
    const current = this.dismissedTips();
    this.dismissedTips.set([...current, tip.id]);
    sessionStorage.setItem('dismissed-tips', JSON.stringify(this.dismissedTips()));
  }

  getAreaWeatherData(areaName: string): WeatherData | null {
    return this.areaWeather()[areaName.toLowerCase()] ?? null;
  }

  handleTipAction(tip: Tip): void {
    if (tip.id === 'no-location') {
      this.openLocationDialog();
    } else if (tip.id === 'paused') {
      this.authService
        .toggleAlerts()
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe(() => this.authService.loadCurrentUser());
    } else if (tip.route) {
      if (tip.queryParams) this.router.navigate([tip.route], { queryParams: tip.queryParams });
      else this.router.navigate([tip.route]);
    }
  }

  navigate(route: string): void {
    this.router.navigate([route]);
  }

  ngOnInit(): void {
    // Re-check on each visit so wizard reappears after navigating away
    this.showOnboarding.set(!localStorage.getItem('poracle-onboarding-complete'));
    this.loadDashboardData();
    // Read fresh rather than trusting whatever an alarm page left behind: the store is upstream's
    // memory and a restart empties it without telling anyone.
    this.mutes.refresh(true);
  }

  openLocationDialog(): void {
    const loc = this.location();
    const dialogRef = this.dialog.open(LocationDialogComponent, {
      data: loc && (loc.latitude !== 0 || loc.longitude !== 0) ? loc : null,
    });
    dialogRef
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result: Location | undefined) => {
        if (result) {
          this.locationService
            .setLocation(result)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe(() => {
              this.location.set(result);
              this.locationAddress.set('');
              this.locationMapUrl.set('');
              if (result.latitude !== 0 || result.longitude !== 0) {
                this.lookUpAddress(result);
                this.locationService
                  .getStaticMapUrl(result.latitude, result.longitude)
                  .pipe(takeUntilDestroyed(this.destroyRef))
                  .subscribe(r => {
                    if (r?.url) this.locationMapUrl.set(r.url);
                  });
                this.loadWeather();
              }
            });
        }
      });
  }

  openQuietList(): void {
    this.dialog.open(QuietListSheetComponent, { width: '420px' });
  }

  startNewAlarm(card: DashboardCard): void {
    this.router.navigate([card.route], { queryParams: ADD_ON_ARRIVAL });
  }

  switchProfile(profile: Profile): void {
    if (profile.active) return;
    this.profileService
      .switchProfile(profile.profileNo)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err =>
          this.snackBar.open(serverReason(err) ?? this.i18n.instant('DASHBOARD.SWITCH_PROFILE_FAILED'), this.i18n.instant('TOAST.OK'), {
            duration: 4000,
          }),
        next: res => {
          if (res.token) {
            this.authService.setToken(res.token);
          }
          this.snackBar.open(this.i18n.instant('DASHBOARD.SWITCH_PROFILE_SUCCESS', { name: profile.name }), this.i18n.instant('TOAST.OK'), {
            duration: 3000,
          });
          this.authService.loadCurrentUser();
          // Reload all dashboard data for the new profile
          this.loadDashboardData();
        },
      });
  }

  toggleIdle(): void {
    this.showAllIdle.update(open => !open);
  }

  private loadAreaWeather(areas: string[], geofences: GeofenceData[]): void {
    if (areas.length === 0 || geofences.length === 0) return;

    // Build a map of geofence name (lowercase) → polygon for quick lookup
    const geoMap = new Map<string, [number, number][]>();
    for (const g of geofences) {
      if (g.path?.length >= 3) {
        geoMap.set(g.name.toLowerCase(), g.path);
      }
    }

    // For each tracked area, compute centroid from its geofence polygon
    const locations = areas
      .map(name => {
        const poly = geoMap.get(name.toLowerCase());
        if (!poly) return null;
        const [lat, lon] = polygonCentroid(poly);
        return { name: name.toLowerCase(), lat, lon };
      })
      .filter((l): l is { name: string; lat: number; lon: number } => l !== null);

    if (locations.length === 0) return;

    this.locationService
      .getAreaWeather(locations)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(results => {
        const map: Record<string, WeatherData> = {};
        for (const r of results) {
          map[r.name] = r.weather;
        }
        this.areaWeather.set(map);
      });
  }

  private loadDashboardData(): void {
    this.dashboardService
      .getCounts()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(c => this.counts.set(c));

    if (this.areasEnabled()) {
      forkJoin([this.areaService.getSelected(), this.areaService.getGeofencePolygons()])
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe(([areas, geofences]) => {
          this.selectedAreas.set(areas);
          this.geofencePolygons.set(geofences);
          this.loadAreaWeather(areas, geofences);
        });
    }

    if (this.profilesEnabled()) {
      this.profileService
        .getAll()
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe(p => this.profiles.set(p));
    }

    if (this.locationEnabled()) {
      this.loadLocation();
    }
  }

  private loadLocation(): void {
    this.locationAddress.set('');
    this.locationMapUrl.set('');
    this.locationService
      .getLocation()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        switchMap(loc => {
          this.location.set(loc);
          if (loc && (loc.latitude !== 0 || loc.longitude !== 0)) {
            this.lookUpAddress(loc);
            this.locationService
              .getStaticMapUrl(loc.latitude, loc.longitude)
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe(result => {
                if (result?.url) this.locationMapUrl.set(result.url);
              });
            this.loadWeather();
          }
          return EMPTY;
        }),
        // Same 403 as #617: without this the disabled-location case threw instead of leaving the
        // location panel empty.
        catchError(() => EMPTY),
      )
      .subscribe();
  }

  private loadWeather(): void {
    this.locationService
      .getWeather()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(w => {
        this.weather.set(w);
      });
  }

  /**
   * The address line under the pin. With geocoding off the request 403s, and the interceptor toasted it
   * on every dashboard load; the card already falls back to coordinates without it.
   */
  private lookUpAddress(loc: Location): void {
    if (!this.geocodingEnabled()) return;
    this.locationService
      .reverseGeocode(loc.latitude, loc.longitude)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(result => {
        if (result?.displayName) this.locationAddress.set(result.displayName);
      });
  }
}
