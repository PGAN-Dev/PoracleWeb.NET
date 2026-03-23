import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterModule } from '@angular/router';
import { switchMap, EMPTY } from 'rxjs';

import { DashboardCounts, Location, Profile } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { AuthService } from '../../core/services/auth.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { LocationService } from '../../core/services/location.service';
import { ProfileService } from '../../core/services/profile.service';
import { LocationDialogComponent } from '../../shared/components/location-dialog/location-dialog.component';
import { OnboardingComponent } from '../../shared/components/onboarding/onboarding.component';

interface DashboardCard {
  colorClass: string;
  icon: string;
  key: keyof DashboardCounts;
  label: string;
  route: string;
  subtitle: string;
}

interface Tip {
  action: string;
  icon: string;
  id: string;
  message: string;
  route: string | null;
  type: 'warning' | 'info';
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatCardModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
    MatSnackBarModule,
    MatTooltipModule,
    MatChipsModule,
    MatDividerModule,
    OnboardingComponent,
    RouterModule,
  ],
  selector: 'app-dashboard',
  standalone: true,
  styleUrl: './dashboard.component.scss',
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent implements OnInit {
  private readonly areaService = inject(AreaService);
  private readonly authService = inject(AuthService);
  private readonly dashboardService = inject(DashboardService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly locationService = inject(LocationService);
  private readonly profileService = inject(ProfileService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  readonly alertsPaused = computed(() => {
    const user = this.authService.user();
    return user ? !user.enabled : false;
  });

  readonly cards: DashboardCard[] = [
    { colorClass: 'card-pokemon', icon: 'catching_pokemon', key: 'pokemon', label: 'Pokemon', route: '/pokemon', subtitle: 'Wild spawns' },
    { colorClass: 'card-raids', icon: 'shield', key: 'raids', label: 'Raids', route: '/raids', subtitle: 'Raid bosses' },
    { colorClass: 'card-eggs', icon: 'egg', key: 'eggs', label: 'Eggs', route: '/raids', subtitle: 'Raid eggs' },
    { colorClass: 'card-quests', icon: 'explore', key: 'quests', label: 'Quests', route: '/quests', subtitle: 'Field research' },
    { colorClass: 'card-invasions', icon: 'warning', key: 'invasions', label: 'Invasions', route: '/invasions', subtitle: 'Team Rocket' },
    { colorClass: 'card-lures', icon: 'location_on', key: 'lures', label: 'Lures', route: '/lures', subtitle: 'Lure modules' },
    { colorClass: 'card-nests', icon: 'park', key: 'nests', label: 'Nests', route: '/nests', subtitle: 'Nesting species' },
    { colorClass: 'card-gyms', icon: 'fitness_center', key: 'gyms', label: 'Gyms', route: '/gyms', subtitle: 'Gym activity' },
  ];

  readonly counts = signal<DashboardCounts | null>(null);
  readonly dismissedTips = signal<string[]>(JSON.parse(sessionStorage.getItem('dismissed-tips') || '[]'));

  readonly location = signal<Location | null>(null);
  readonly locationAddress = signal<string>('');
  readonly locationMapUrl = signal<string>('');

  readonly profileNo = computed(() => this.authService.user()?.profileNo ?? 1);
  readonly profiles = signal<Profile[]>([]);
  readonly profileName = computed(() => {
    const profiles = this.profiles();
    if (profiles.length === 0) return 'Default';
    const no = this.profileNo();
    const match = profiles.find(p => p.profileNo === no);
    return match?.name ?? 'Default';
  });

  readonly selectedAreas = signal<string[]>([]);
  readonly showOnboarding = signal(!localStorage.getItem('poracle-onboarding-complete'));

  readonly skeletonItems = [1, 2, 3, 4, 5, 6, 7, 8];

  readonly tips = computed(() => {
    const tips: Tip[] = [];

    if (!this.userLocation()) {
      tips.push({
        id: 'no-location',
        action: 'Set Location',
        icon: 'location_off',
        message: 'Set your location to enable distance-based notifications',
        route: null,
        type: 'warning',
      });
    }

    if (this.selectedAreas().length === 0) {
      tips.push({
        id: 'no-areas',
        action: 'Set Up Areas',
        icon: 'map',
        message: 'Configure your areas to receive area-based notifications',
        route: '/areas',
        type: 'info',
      });
    }

    const c = this.counts();
    if (c && Object.values(c).every(v => v === 0)) {
      tips.push({
        id: 'no-alarms',
        action: 'Add Pokemon Alarm',
        icon: 'add_alert',
        message: 'You have no active alarms yet. Start by adding Pokemon or Raid alerts!',
        route: '/pokemon',
        type: 'info',
      });
    }

    return tips.filter(t => !this.dismissedTips().includes(t.id));
  });

  readonly totalAlarms = computed(() => {
    const c = this.counts();
    if (!c) return 0;
    return (
      (c.pokemon ?? 0) +
      (c.raids ?? 0) +
      (c.eggs ?? 0) +
      (c.quests ?? 0) +
      (c.invasions ?? 0) +
      (c.lures ?? 0) +
      (c.nests ?? 0) +
      (c.gyms ?? 0)
    );
  });

  readonly userLocation = computed(() => {
    const loc = this.location();
    if (!loc) return false;
    return loc.latitude !== 0 || loc.longitude !== 0;
  });

  readonly username = computed(() => this.authService.user()?.username ?? 'Trainer');

  dismissTip(tip: Tip): void {
    const current = this.dismissedTips();
    this.dismissedTips.set([...current, tip.id]);
    sessionStorage.setItem('dismissed-tips', JSON.stringify(this.dismissedTips()));
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
      this.router.navigate([tip.route]);
    }
  }

  navigate(route: string): void {
    this.router.navigate([route]);
  }

  ngOnInit(): void {
    // Re-check on each visit so wizard reappears after navigating away
    this.showOnboarding.set(!localStorage.getItem('poracle-onboarding-complete'));
    this.loadDashboardData();
  }

  openLocationDialog(): void {
    const loc = this.location();
    const dialogRef = this.dialog.open(LocationDialogComponent, {
      width: '600px',
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
                this.locationService
                  .reverseGeocode(result.latitude, result.longitude)
                  .pipe(takeUntilDestroyed(this.destroyRef))
                  .subscribe(r => {
                    if (r?.display_name) this.locationAddress.set(r.display_name);
                  });
                this.locationService
                  .getStaticMapUrl(result.latitude, result.longitude)
                  .pipe(takeUntilDestroyed(this.destroyRef))
                  .subscribe(r => {
                    if (r?.url) this.locationMapUrl.set(r.url);
                  });
              }
            });
        }
      });
  }

  switchProfile(profile: Profile): void {
    if (profile.active) return;
    this.profileService
      .switchProfile(profile.profileNo)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.snackBar.open('Failed to switch profile', 'OK', { duration: 3000 }),
        next: res => {
          if (res.token) {
            this.authService.setToken(res.token);
          }
          this.snackBar.open(`Switched to "${profile.name}"`, 'OK', { duration: 3000 });
          this.authService.loadCurrentUser();
          // Reload all dashboard data for the new profile
          this.loadDashboardData();
        },
      });
  }

  private loadDashboardData(): void {
    this.dashboardService
      .getCounts()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(c => this.counts.set(c));

    this.areaService
      .getSelected()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(a => this.selectedAreas.set(a));

    this.profileService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(p => this.profiles.set(p));

    this.loadLocation();
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
            this.locationService
              .reverseGeocode(loc.latitude, loc.longitude)
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe(result => {
                if (result?.display_name) this.locationAddress.set(result.display_name);
              });
            this.locationService
              .getStaticMapUrl(loc.latitude, loc.longitude)
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe(result => {
                if (result?.url) this.locationMapUrl.set(result.url);
              });
          }
          return EMPTY;
        }),
      )
      .subscribe();
  }
}
