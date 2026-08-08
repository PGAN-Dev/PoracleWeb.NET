import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

import { CleaningService, CleanAlarmType } from '../../core/services/cleaning.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { I18nService } from '../../core/services/i18n.service';
import { SettingsService } from '../../core/services/settings.service';

interface CleaningItem {
  color: string;
  descriptionKey: string;
  /**
   * The site setting that switches this alarm type off. Eggs share the raid switch — there is no
   * separate disable_eggs, and eggs share the raid UI. See #509.
   */
  disableKey: string;
  enabled: ReturnType<typeof signal<boolean>>;
  hasAlarms: ReturnType<typeof signal<boolean>>;
  icon: string;
  labelKey: string;
  type: CleanAlarmType;
}

/** Cleaning row type -> the matching field on the dashboard counts payload. */
const DASHBOARD_COUNT_KEYS: Record<string, string> = {
  maxbattles: 'maxBattles',
  monsters: 'pokemon',
};

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    TranslatePipe,
  ],
  selector: 'app-cleaning',
  standalone: true,
  styleUrl: './cleaning.component.scss',
  templateUrl: './cleaning.component.html',
})
export class CleaningComponent implements OnInit {
  private readonly cleaningService = inject(CleaningService);
  private readonly dashboardService = inject(DashboardService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly settingsService = inject(SettingsService);
  private readonly snackBar = inject(MatSnackBar);

  readonly cleaningItems: CleaningItem[] = [
    {
      color: '#4CAF50',
      descriptionKey: 'CLEANING.POKEMON_DESC',
      disableKey: 'disable_mons',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'catching_pokemon',
      labelKey: 'NAV.POKEMON',
      type: 'monsters',
    },
    {
      color: '#F44336',
      descriptionKey: 'CLEANING.RAIDS_DESC',
      disableKey: 'disable_raids',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'shield',
      labelKey: 'NAV.RAIDS',
      type: 'raids',
    },
    {
      color: '#FF9800',
      descriptionKey: 'CLEANING.EGGS_DESC',
      disableKey: 'disable_raids',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'egg',
      labelKey: 'CLEANING.LABEL_EGGS',
      type: 'eggs',
    },
    {
      color: '#9C27B0',
      descriptionKey: 'CLEANING.QUESTS_DESC',
      disableKey: 'disable_quests',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'assignment',
      labelKey: 'NAV.QUESTS',
      type: 'quests',
    },
    {
      color: '#607D8B',
      descriptionKey: 'CLEANING.INVASIONS_DESC',
      disableKey: 'disable_invasions',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'warning',
      labelKey: 'NAV.INVASIONS',
      type: 'invasions',
    },
    {
      color: '#E91E63',
      descriptionKey: 'CLEANING.LURES_DESC',
      disableKey: 'disable_lures',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'place',
      labelKey: 'NAV.LURES',
      type: 'lures',
    },
    {
      color: '#8BC34A',
      descriptionKey: 'CLEANING.NESTS_DESC',
      disableKey: 'disable_nests',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'park',
      labelKey: 'NAV.NESTS',
      type: 'nests',
    },
    {
      color: '#00BCD4',
      descriptionKey: 'CLEANING.GYMS_DESC',
      disableKey: 'disable_gyms',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'fitness_center',
      labelKey: 'NAV.GYMS',
      type: 'gyms',
    },
    {
      color: '#d500f9',
      descriptionKey: 'CLEANING.MAX_BATTLES_DESC',
      disableKey: 'disable_maxbattles',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'flash_on',
      labelKey: 'NAV.MAX_BATTLES',
      type: 'maxbattles',
    },
  ];

  // The page rendered a row per alarm type regardless of the site settings, and the toggle's only
  // disabled condition was a request already being in flight. Pressing a row for a type an admin had
  // switched off produced a 403 whose only outcome was an error toast. See #509.
  readonly visibleItems = computed(() => this.cleaningItems.filter(i => !this.settingsService.isDisabled(i.disableKey)));

  readonly allEnabled = computed(() => this.visibleItems().every(i => i.enabled()));
  readonly loading = signal(true);
  readonly toggling = signal(false);

  ngOnInit(): void {
    this.loadStatus();
  }

  toggleAll(enabled: boolean): void {
    this.toggling.set(true);
    this.cleaningService
      .toggleAll(enabled)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.toggling.set(false);
          this.snackBar.open(this.i18n.instant('CLEANING.SNACK_FAILED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: result => {
          for (const item of this.cleaningItems) {
            item.enabled.set(enabled);
          }
          this.toggling.set(false);
          const action = this.i18n.instant(enabled ? 'CLEANING.ENABLED' : 'CLEANING.DISABLED');
          this.snackBar.open(
            this.i18n.instant('CLEANING.SNACK_TOGGLE_ALL', { action, count: result.updated }),
            this.i18n.instant('TOAST.OK'),
            { duration: 3000 },
          );
        },
      });
  }

  toggleClean(item: CleaningItem, enabled: boolean): void {
    this.toggling.set(true);
    this.cleaningService
      .toggleClean(item.type, enabled)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.toggling.set(false);
          this.snackBar.open(this.i18n.instant('CLEANING.SNACK_FAILED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: result => {
          item.enabled.set(enabled);
          this.toggling.set(false);
          const action = this.i18n.instant(enabled ? 'CLEANING.ENABLED' : 'CLEANING.DISABLED');
          const type = this.i18n.instant(item.labelKey);
          this.snackBar.open(
            this.i18n.instant('CLEANING.SNACK_TOGGLE_TYPE', { action, count: result.updated, type }),
            this.i18n.instant('TOAST.OK'),
            { duration: 3000 },
          );
        },
      });
  }

  private loadStatus(): void {
    this.dashboardService
      .getCounts()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: counts => {
          for (const item of this.cleaningItems) {
            // The dashboard counts are camelCase; the cleaning rows use the API route names. Without
            // this map, 'maxbattles' never matched 'maxBattles' and the row was permanently greyed out.
            const key = DASHBOARD_COUNT_KEYS[item.type] ?? item.type;
            const count = (counts as unknown as Record<string, number>)[key] ?? 0;
            item.hasAlarms.set(count > 0);
          }
        },
      });

    this.cleaningService
      .getStatus()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: status => {
          for (const item of this.cleaningItems) {
            if (status[item.type] !== undefined) {
              item.enabled.set(status[item.type]);
            }
          }
          this.loading.set(false);
        },
      });
  }
}
