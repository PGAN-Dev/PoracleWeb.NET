import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggle, MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

import { CleaningService, CleanAlarmType, CleanCount, CleanRefusal } from '../../core/services/cleaning.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { I18nService } from '../../core/services/i18n.service';
import { SettingsService } from '../../core/services/settings.service';
import { TranslatePluralPipe } from '../../shared/pipes/translate-plural.pipe';
import { serverReason } from '../../shared/utils/server-reason';

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
  /** Set when some but not all of the type's alarms are clean, which the toggle alone reads as off. */
  partial: ReturnType<typeof signal<CleanCount | null>>;
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
    TranslatePluralPipe,
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
      partial: signal<CleanCount | null>(null),
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
      partial: signal<CleanCount | null>(null),
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
      partial: signal<CleanCount | null>(null),
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
      partial: signal<CleanCount | null>(null),
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
      partial: signal<CleanCount | null>(null),
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
      partial: signal<CleanCount | null>(null),
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
      partial: signal<CleanCount | null>(null),
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
      partial: signal<CleanCount | null>(null),
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
      partial: signal<CleanCount | null>(null),
      type: 'maxbattles',
    },
  ];

  // The page rendered a row per alarm type regardless of the site settings, and the toggle's only
  // disabled condition was a request already being in flight. Pressing a row for a type an admin had
  // switched off produced a 403 whose only outcome was an error toast. See #509.
  readonly visibleItems = computed(() => this.cleaningItems.filter(i => !this.settingsService.isDisabled(i.disableKey)));

  /**
   * The rows cleaning can act on: shown, and holding at least one alarm. A type with no alarms is not
   * applicable -- the API reports it as not clean, since there is nothing to be clean -- so counting
   * it kept the header on "Enable All" for everyone without alarms of every type.
   */
  readonly applicableItems = computed(() => this.visibleItems().filter(i => i.hasAlarms()));

  readonly allEnabled = computed(() => {
    const applicable = this.applicableItems();
    return applicable.length > 0 && applicable.every(i => i.enabled());
  });

  readonly hasApplicable = computed(() => this.applicableItems().length > 0);
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
        error: (err: unknown) => {
          this.toggling.set(false);
          this.snackBar.open(serverReason(err) ?? this.i18n.instant('CLEANING.SNACK_FAILED'), this.i18n.instant('TOAST.OK'), {
            duration: 3000,
          });
        },
        next: result => {
          // Only the rows the server wrote. An empty type has nothing to flip, and a disabled one was
          // skipped, so marking either on showed a state a reload would take back.
          // With a refusal the status read below says which types actually moved; guessing "all" here is
          // what let a half-refused toggle report full success.
          if ((result.refused ?? []).length === 0) {
            for (const item of this.applicableItems()) {
              item.enabled.set(enabled);
              item.partial.set(null);
            }
          }
          this.toggling.set(false);
          this.readStatus();
          const action = this.i18n.instant(enabled ? 'CLEANING.ENABLED' : 'CLEANING.DISABLED');
          const refused = result.refused ?? [];
          this.snackBar.open(
            this.withRefusals(this.i18n.plural('CLEANING.SNACK_TOGGLE_ALL', result.updated, { action }), refused),
            this.i18n.instant('TOAST.OK'),
            { duration: refused.length > 0 ? 8000 : 3000 },
          );
        },
      });
  }

  toggleClean(item: CleaningItem, enabled: boolean, toggle?: MatSlideToggle): void {
    this.toggling.set(true);
    this.cleaningService
      .toggleClean(item.type, enabled)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: (err: unknown) => {
          this.toggling.set(false);
          this.snackBar.open(serverReason(err) ?? this.i18n.instant('CLEANING.SNACK_FAILED'), this.i18n.instant('TOAST.OK'), {
            duration: 3000,
          });
        },
        next: result => {
          const refused = result.refused ?? [];
          if (refused.length === 0) {
            item.enabled.set(enabled);
            item.partial.set(null);
          } else if (toggle) {
            // The switch has already moved under the user's finger; the signal has not, so binding alone
            // would leave it showing a state the server refused. The status read below settles the rest.
            toggle.checked = item.enabled();
          }
          this.toggling.set(false);
          this.readStatus();
          const action = this.i18n.instant(enabled ? 'CLEANING.ENABLED' : 'CLEANING.DISABLED');
          const type = this.i18n.instant(item.labelKey);
          this.snackBar.open(
            this.withRefusals(this.i18n.plural('CLEANING.SNACK_TOGGLE_TYPE', result.updated, { action, type }), refused),
            this.i18n.instant('TOAST.OK'),
            { duration: refused.length > 0 ? 8000 : 3000 },
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

    this.readStatus();
  }

  /**
   * Reads which types are clean. Run again after every toggle: PoracleNG can refuse some rules, which keep
   * their old setting, so assuming the whole type moved could hide a type that is still only partly clean.
   */
  private readStatus(): void {
    this.cleaningService
      .getStatus()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => this.loading.set(false),
        next: status => {
          for (const item of this.cleaningItems) {
            const flag = status[item.type];
            if (typeof flag === 'boolean') {
              item.enabled.set(flag);
            }
            const count = status.counts?.[item.type];
            item.partial.set(count && count.clean > 0 && count.clean < count.total ? count : null);
          }
          this.loading.set(false);
        },
      });
  }

  /**
   * The success line, then one clause per kind of refusal. A conflict is a rule that would match another
   * of the member's alarms once changed; anything else is a refusal the server did not explain further.
   */
  private withRefusals(success: string, refused: CleanRefusal[]): string {
    if (refused.length === 0) return success;
    const conflicts = refused.filter(r => r.reason === 'conflict').length;
    const others = refused.length - conflicts;
    const clauses: string[] = [];
    if (conflicts > 0) clauses.push(this.i18n.plural('CLEANING.REFUSED_CONFLICT', conflicts));
    if (others > 0) clauses.push(this.i18n.plural('CLEANING.REFUSED_OTHER', others));
    const lead = /[.!?]$/.test(success) ? success : `${success}.`;
    return [lead, ...clauses].join(' ');
  }
}
