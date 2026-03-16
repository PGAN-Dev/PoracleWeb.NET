import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { switchMap, filter, EMPTY } from 'rxjs';
import { DashboardService } from '../../core/services/dashboard.service';
import { AreaService } from '../../core/services/area.service';
import { LocationService } from '../../core/services/location.service';
import { AuthService } from '../../core/services/auth.service';
import { DashboardCounts, Location } from '../../core/models';

interface DashboardCard {
  key: keyof DashboardCounts;
  label: string;
  subtitle: string;
  icon: string;
  colorClass: string;
  route: string;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MatIconModule, MatButtonModule, MatTooltipModule, MatChipsModule, MatDividerModule],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>Dashboard</h1>
        <p class="dashboard-subtitle">Welcome back, {{ username() }}</p>
        <p class="page-description">Your notification alarm overview. Click any card to manage alarms, or use Quick Actions to get started.</p>
      </div>
    </div>

    <div class="dashboard-content">
      <!-- Status Overview Row -->
      <div class="status-row">
        <!-- Location Card -->
        <mat-card class="status-card location-card-full" (click)="navigate('/areas')">
          @if (locationMapUrl()) {
            <img [src]="locationMapUrl()" class="location-map-thumb" alt="Location" loading="lazy" />
          }
          <div class="location-card-body">
            <div class="status-icon-wrap location-icon">
              <mat-icon>my_location</mat-icon>
            </div>
            <div class="status-info">
              <span class="status-label">Location</span>
              @if (location(); as loc) {
                @if (locationAddress()) {
                  <span class="status-value status-address" [title]="loc.latitude.toFixed(4) + ', ' + loc.longitude.toFixed(4)">{{ locationAddress() }}</span>
                } @else {
                  <span class="status-value">{{ loc.latitude.toFixed(4) }}, {{ loc.longitude.toFixed(4) }}</span>
                }
              } @else {
                <span class="status-value not-set">Not set</span>
              }
            </div>
            <mat-icon class="status-arrow">chevron_right</mat-icon>
          </div>
        </mat-card>

        <!-- Areas Card -->
        <mat-card class="status-card areas-card" (click)="navigate('/areas')">
          <div class="status-icon-wrap areas-icon">
            <mat-icon>map</mat-icon>
          </div>
          <div class="status-info">
            <span class="status-label">Active Areas</span>
            @if (selectedAreas().length > 0) {
              <span class="status-value">{{ selectedAreas().length }} area(s)</span>
            } @else {
              <span class="status-value not-set">No areas selected</span>
            }
          </div>
          <mat-icon class="status-arrow">chevron_right</mat-icon>
        </mat-card>

        <!-- Profile Card -->
        <mat-card class="status-card profile-card" (click)="navigate('/profiles')">
          <div class="status-icon-wrap profile-icon">
            <mat-icon>person</mat-icon>
          </div>
          <div class="status-info">
            <span class="status-label">Profile</span>
            <span class="status-value">Profile {{ profileNo() }}</span>
          </div>
          <mat-icon class="status-arrow">chevron_right</mat-icon>
        </mat-card>
      </div>

      <!-- Areas Chips -->
      @if (selectedAreas().length > 0) {
        <mat-card class="areas-detail-card">
          <mat-card-content>
            <div class="areas-header">
              <mat-icon>place</mat-icon>
              <span>Tracking areas</span>
            </div>
            <mat-chip-set class="area-chips">
              @for (area of selectedAreas(); track area) {
                <mat-chip highlighted color="primary">{{ area }}</mat-chip>
              }
            </mat-chip-set>
          </mat-card-content>
        </mat-card>
      }

      <!-- Alarm Counts Section -->
      <div class="section-header">
        <h2>Active Filters</h2>
        <span class="total-count">{{ totalAlarms() }} total alarms</span>
      </div>

      <div class="dashboard-grid">
        @if (counts(); as c) {
          @for (card of cards; track card.key) {
            <mat-card
              class="dashboard-card"
              [class]="card.colorClass"
              [class.muted]="c[card.key] === 0"
              (click)="navigate(card.route)"
            >
              <div class="card-header-row">
                <div class="card-icon-wrap" [class.muted-icon]="c[card.key] === 0">
                  <mat-icon>{{ card.icon }}</mat-icon>
                </div>
              </div>
              <mat-card-content>
                <span class="count" [class.count-zero]="c[card.key] === 0">{{ c[card.key] }}</span>
                <span class="label">{{ card.label }}</span>
                <span class="subtitle">{{ card.subtitle }}</span>
              </mat-card-content>
            </mat-card>
          }
        } @else {
          @for (i of skeletonItems; track i) {
            <mat-card class="dashboard-card skeleton">
              <mat-card-content>
                <div class="skeleton-line"></div>
                <div class="skeleton-count"></div>
              </mat-card-content>
            </mat-card>
          }
        }
      </div>

      <!-- Quick Actions -->
      <div class="section-header">
        <h2>Quick Actions</h2>
      </div>
      <div class="quick-actions">
        <button mat-stroked-button (click)="navigate('/pokemon')" class="action-btn">
          <mat-icon>catching_pokemon</mat-icon> Add Pokemon
        </button>
        <button mat-stroked-button (click)="navigate('/raids')" class="action-btn">
          <mat-icon>shield</mat-icon> Add Raid
        </button>
        <button mat-stroked-button (click)="navigate('/quests')" class="action-btn">
          <mat-icon>explore</mat-icon> Add Quest
        </button>
        <button mat-stroked-button (click)="navigate('/areas')" class="action-btn">
          <mat-icon>map</mat-icon> Manage Areas
        </button>
        <button mat-stroked-button (click)="navigate('/cleaning')" class="action-btn">
          <mat-icon>cleaning_services</mat-icon> Cleaning
        </button>
      </div>
    </div>
  `,
  styles: [
    `
      .page-header { display: flex; justify-content: space-between; align-items: flex-start; padding: 24px 24px 0; gap: 16px; }
      .page-header-text { flex: 1; min-width: 0; }
      .page-header h1 { margin: 0; font-size: 24px; font-weight: 400; }
      .dashboard-subtitle { margin: 4px 0 0; color: var(--text-secondary, rgba(0,0,0,0.54)); font-size: 14px; }
      .page-description { margin: 4px 0 0; color: var(--text-secondary, rgba(0,0,0,0.54)); font-size: 13px; line-height: 1.5; border-left: 3px solid #1976d2; padding-left: 12px; }

      .dashboard-content { padding: 16px 24px 24px; max-width: 1200px; }

      /* Status Row */
      .status-row {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
        gap: 16px;
        margin-bottom: 20px;
      }
      .status-card {
        display: flex;
        align-items: center;
        padding: 16px 20px;
        cursor: pointer;
        transition: transform 0.15s, box-shadow 0.15s;
        gap: 16px;
      }
      .status-card:hover {
        transform: translateY(-2px);
        box-shadow: 0 4px 16px rgba(0,0,0,0.1);
      }
      .status-icon-wrap {
        width: 44px;
        height: 44px;
        border-radius: 12px;
        display: flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
      }
      .location-icon { background: #e3f2fd; color: #1565c0; }
      .areas-icon { background: #e8f5e9; color: #2e7d32; }
      .profile-icon { background: #f3e5f5; color: #7b1fa2; }
      .status-info { flex: 1; display: flex; flex-direction: column; }
      .status-label { font-size: 12px; color: var(--text-secondary, rgba(0,0,0,0.54)); text-transform: uppercase; letter-spacing: 0.5px; }
      .status-value { font-size: 16px; font-weight: 500; margin-top: 2px; }
      .status-value.not-set { color: var(--text-hint, rgba(0,0,0,0.38)); font-style: italic; font-weight: 400; }
      .status-value.status-address { font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 200px; cursor: help; }
      .status-arrow { color: var(--text-hint, rgba(0,0,0,0.24)); }

      .location-card-full {
        padding: 0 !important;
        overflow: hidden;
        cursor: pointer;
        transition: transform 0.15s, box-shadow 0.15s;
      }
      .location-card-full:hover {
        transform: translateY(-2px);
        box-shadow: 0 4px 16px rgba(0,0,0,0.1);
      }
      .location-map-thumb {
        width: 100%;
        height: 120px;
        object-fit: cover;
        display: block;
      }
      .location-card-body {
        display: flex;
        align-items: center;
        padding: 12px 16px;
        gap: 16px;
      }

      /* Areas Detail */
      .areas-detail-card { margin-bottom: 20px; }
      .areas-header {
        display: flex;
        align-items: center;
        gap: 8px;
        margin-bottom: 12px;
        color: var(--text-secondary, rgba(0,0,0,0.54));
        font-size: 13px;
        text-transform: uppercase;
        letter-spacing: 0.5px;
      }
      .areas-header mat-icon { font-size: 18px; width: 18px; height: 18px; }
      .area-chips { display: flex; flex-wrap: wrap; gap: 4px; }

      /* Section Header */
      .section-header {
        display: flex;
        justify-content: space-between;
        align-items: baseline;
        margin-bottom: 12px;
      }
      .section-header h2 { margin: 0; font-size: 18px; font-weight: 500; }
      .total-count { font-size: 13px; color: var(--text-secondary, rgba(0,0,0,0.54)); }

      /* Alarm Grid */
      .dashboard-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
        gap: 16px;
        margin-bottom: 28px;
      }
      .dashboard-card {
        cursor: pointer;
        transition: transform 0.2s, box-shadow 0.2s;
        border-top: 4px solid var(--mat-sys-primary, #1976d2);
        position: relative;
      }
      .dashboard-card:hover { transform: translateY(-3px); box-shadow: 0 6px 20px rgba(0,0,0,0.12); }
      .dashboard-card.muted { opacity: 0.55; border-top-color: var(--text-hint, rgba(0,0,0,0.2)) !important; }
      .dashboard-card.muted:hover { opacity: 0.75; }
      .card-pokemon { border-top-color: #4caf50; }
      .card-raids { border-top-color: #f44336; }
      .card-eggs { border-top-color: #ff9800; }
      .card-quests { border-top-color: #9c27b0; }
      .card-invasions { border-top-color: #607d8b; }
      .card-lures { border-top-color: #e91e63; }
      .card-nests { border-top-color: #8bc34a; }
      .card-gyms { border-top-color: #00bcd4; }

      .card-header-row { display: flex; justify-content: space-between; align-items: flex-start; padding: 12px 12px 0; }
      .card-icon-wrap {
        width: 36px; height: 36px; border-radius: 10px;
        display: flex; align-items: center; justify-content: center;
        background: var(--mat-sys-primary-container, rgba(25,118,210,0.12));
        color: var(--mat-sys-on-primary-container, #1976d2);
      }
      .card-icon-wrap.muted-icon { background: var(--skeleton-bg, rgba(0,0,0,0.06)); color: var(--text-hint, rgba(0,0,0,0.38)); }
      .card-icon-wrap mat-icon { font-size: 20px; width: 20px; height: 20px; }

      .count { display: block; font-size: 32px; font-weight: 300; text-align: center; margin: 8px 0 2px; line-height: 1.1; }
      .count-zero { color: var(--text-hint, rgba(0,0,0,0.3)); }
      .label { display: block; text-align: center; font-size: 14px; font-weight: 500; margin-bottom: 1px; }
      .subtitle { display: block; text-align: center; color: var(--text-secondary, rgba(0,0,0,0.54)); font-size: 11px; margin-bottom: 8px; }

      /* Quick Actions */
      .quick-actions { display: flex; flex-wrap: wrap; gap: 10px; }
      .action-btn { text-transform: none; }
      .action-btn mat-icon { font-size: 18px; width: 18px; height: 18px; margin-right: 4px; }

      /* Skeleton */
      .skeleton mat-card-content { padding: 16px; }
      .skeleton-line { height: 20px; background: var(--skeleton-bg, #e0e0e0); border-radius: 4px; margin-bottom: 16px; animation: pulse 1.5s infinite; }
      .skeleton-count { height: 40px; width: 60px; margin: 0 auto; background: var(--skeleton-bg, #e0e0e0); border-radius: 4px; animation: pulse 1.5s infinite; }
      @keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.5; } }

      @media (max-width: 599px) {
        .status-row { grid-template-columns: 1fr; }
        .dashboard-grid { grid-template-columns: repeat(2, 1fr); }
      }
    `,
  ],
})
export class DashboardComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dashboardService = inject(DashboardService);
  private readonly areaService = inject(AreaService);
  private readonly locationService = inject(LocationService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly counts = signal<DashboardCounts | null>(null);
  readonly selectedAreas = signal<string[]>([]);
  readonly location = signal<Location | null>(null);
  readonly locationAddress = signal<string>('');
  readonly locationMapUrl = signal<string>('');
  readonly skeletonItems = [1, 2, 3, 4, 5, 6, 7, 8];

  readonly username = computed(() => this.authService.user()?.username ?? 'Trainer');
  readonly profileNo = computed(() => this.authService.user()?.profileNo ?? 1);
  readonly totalAlarms = computed(() => {
    const c = this.counts();
    if (!c) return 0;
    return (c.pokemon ?? 0) + (c.raids ?? 0) + (c.eggs ?? 0) + (c.quests ?? 0) +
           (c.invasions ?? 0) + (c.lures ?? 0) + (c.nests ?? 0) + (c.gyms ?? 0);
  });

  readonly cards: DashboardCard[] = [
    { key: 'pokemon', label: 'Pokemon', subtitle: 'Wild spawns', icon: 'catching_pokemon', colorClass: 'card-pokemon', route: '/pokemon' },
    { key: 'raids', label: 'Raids', subtitle: 'Raid bosses', icon: 'shield', colorClass: 'card-raids', route: '/raids' },
    { key: 'eggs', label: 'Eggs', subtitle: 'Raid eggs', icon: 'egg', colorClass: 'card-eggs', route: '/raids' },
    { key: 'quests', label: 'Quests', subtitle: 'Field research', icon: 'explore', colorClass: 'card-quests', route: '/quests' },
    { key: 'invasions', label: 'Invasions', subtitle: 'Team Rocket', icon: 'warning', colorClass: 'card-invasions', route: '/invasions' },
    { key: 'lures', label: 'Lures', subtitle: 'Lure modules', icon: 'location_on', colorClass: 'card-lures', route: '/lures' },
    { key: 'nests', label: 'Nests', subtitle: 'Nesting species', icon: 'park', colorClass: 'card-nests', route: '/nests' },
    { key: 'gyms', label: 'Gyms', subtitle: 'Gym activity', icon: 'fitness_center', colorClass: 'card-gyms', route: '/gyms' },
  ];

  ngOnInit(): void {
    this.dashboardService.getCounts().pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(c => this.counts.set(c));

    this.areaService.getSelected().pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(a => this.selectedAreas.set(a));

    this.locationService.getLocation().pipe(
      takeUntilDestroyed(this.destroyRef),
      switchMap((loc) => {
        this.location.set(loc);
        if (loc && (loc.latitude !== 0 || loc.longitude !== 0)) {
          this.locationService.reverseGeocode(loc.latitude, loc.longitude).pipe(
            takeUntilDestroyed(this.destroyRef)
          ).subscribe((result) => {
            if (result?.display_name) this.locationAddress.set(result.display_name);
          });
          this.locationService.getStaticMapUrl(loc.latitude, loc.longitude).pipe(
            takeUntilDestroyed(this.destroyRef)
          ).subscribe((result) => {
            if (result?.url) this.locationMapUrl.set(result.url);
          });
        }
        return EMPTY;
      }),
    ).subscribe();
  }

  navigate(route: string): void {
    this.router.navigate([route]);
  }
}
