import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { CleaningService, CleanAlarmType } from '../../core/services/cleaning.service';
import { DashboardService } from '../../core/services/dashboard.service';

interface CleaningItem {
  color: string;
  description: string;
  enabled: ReturnType<typeof signal<boolean>>;
  hasAlarms: ReturnType<typeof signal<boolean>>;
  icon: string;
  label: string;
  type: CleanAlarmType;
}

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
  private readonly snackBar = inject(MatSnackBar);

  readonly cleaningItems: CleaningItem[] = [
    {
      color: '#4CAF50',
      description: 'Auto-delete spawn notifications when the Pokemon despawns',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'catching_pokemon',
      label: 'Pokemon',
      type: 'monsters',
    },
    {
      color: '#F44336',
      description: 'Auto-delete raid notifications when the raid ends',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'shield',
      label: 'Raids',
      type: 'raids',
    },
    {
      color: '#FF9800',
      description: 'Auto-delete egg notifications when the egg hatches',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'egg',
      label: 'Eggs',
      type: 'eggs',
    },
    {
      color: '#9C27B0',
      description: 'Auto-delete quest notifications at midnight',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'assignment',
      label: 'Quests',
      type: 'quests',
    },
    {
      color: '#607D8B',
      description: 'Auto-delete invasion notifications when the grunt leaves',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'warning',
      label: 'Invasions',
      type: 'invasions',
    },
    {
      color: '#E91E63',
      description: 'Auto-delete lure notifications when the lure expires',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'place',
      label: 'Lures',
      type: 'lures',
    },
    {
      color: '#8BC34A',
      description: 'Auto-delete nest notifications when nests rotate',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'park',
      label: 'Nests',
      type: 'nests',
    },
    {
      color: '#00BCD4',
      description: 'Auto-delete gym notifications when gym status changes',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'fitness_center',
      label: 'Gyms',
      type: 'gyms',
    },
    {
      color: '#795548',
      description: 'Auto-delete fort change notifications after expiry',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'domain',
      label: 'Fort Changes',
      type: 'fortchanges',
    },
  ];

  readonly allEnabled = computed(() => this.cleaningItems.every(i => i.enabled()));
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
          this.snackBar.open('Failed to update cleaning', 'OK', { duration: 3000 });
        },
        next: result => {
          for (const item of this.cleaningItems) {
            item.enabled.set(enabled);
          }
          this.toggling.set(false);
          const action = enabled ? 'enabled' : 'disabled';
          this.snackBar.open(`Cleaning ${action} for all types (${result.updated} alarms updated)`, 'OK', { duration: 3000 });
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
          this.snackBar.open(`Failed to update cleaning for ${item.label}`, 'OK', {
            duration: 3000,
          });
        },
        next: result => {
          item.enabled.set(enabled);
          this.toggling.set(false);
          const action = enabled ? 'enabled' : 'disabled';
          this.snackBar.open(`Cleaning ${action} for ${item.label} (${result.updated} alarms updated)`, 'OK', { duration: 3000 });
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
            const key = item.type === 'monsters' ? 'pokemon' : item.type === 'fortchanges' ? 'fortChanges' : item.type;
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
