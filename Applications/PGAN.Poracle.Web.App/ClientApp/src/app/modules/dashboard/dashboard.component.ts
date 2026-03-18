import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';
import { switchMap, EMPTY } from 'rxjs';

import { DashboardCounts, Location } from '../../core/models';
import { AreaService } from '../../core/services/area.service';
import { AuthService } from '../../core/services/auth.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { LocationService } from '../../core/services/location.service';

interface DashboardCard {
  colorClass: string;
  icon: string;
  key: keyof DashboardCounts;
  label: string;
  route: string;
  subtitle: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatTooltipModule, MatChipsModule, MatDividerModule],
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
  private readonly locationService = inject(LocationService);
  private readonly router = inject(Router);

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
  readonly location = signal<Location | null>(null);
  readonly locationAddress = signal<string>('');
  readonly locationMapUrl = signal<string>('');
  readonly profileNo = computed(() => this.authService.user()?.profileNo ?? 1);

  readonly selectedAreas = signal<string[]>([]);
  readonly skeletonItems = [1, 2, 3, 4, 5, 6, 7, 8];
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

  readonly username = computed(() => this.authService.user()?.username ?? 'Trainer');

  navigate(route: string): void {
    this.router.navigate([route]);
  }

  ngOnInit(): void {
    this.dashboardService
      .getCounts()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(c => this.counts.set(c));

    this.areaService
      .getSelected()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(a => this.selectedAreas.set(a));

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
