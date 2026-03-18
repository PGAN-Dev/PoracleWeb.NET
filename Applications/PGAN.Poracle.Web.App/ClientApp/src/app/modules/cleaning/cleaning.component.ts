import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

import { CleaningService, CleanAlarmType } from '../../core/services/cleaning.service';
import { DashboardService } from '../../core/services/dashboard.service';

interface CleaningItem {
  color: string;
  enabled: ReturnType<typeof signal<boolean>>;
  hasAlarms: ReturnType<typeof signal<boolean>>;
  icon: string;
  label: string;
  type: CleanAlarmType;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule, MatSlideToggleModule, MatIconModule, MatSnackBarModule, MatProgressSpinnerModule],
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
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'catching_pokemon',
      label: 'Pokemon Alarms',
      type: 'monsters',
    },
    {
      color: '#F44336',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'shield',
      label: 'Raids & Eggs',
      type: 'raids',
    },
    {
      color: '#9C27B0',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'explore',
      label: 'Quests',
      type: 'quests',
    },
    {
      color: '#607D8B',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'warning',
      label: 'Invasions',
      type: 'invasions',
    },
    {
      color: '#E91E63',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'location_on',
      label: 'Lures',
      type: 'lures',
    },
    {
      color: '#8BC34A',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'park',
      label: 'Nests',
      type: 'nests',
    },
    {
      color: '#00BCD4',
      enabled: signal(false),
      hasAlarms: signal(false),
      icon: 'fitness_center',
      label: 'Gyms',
      type: 'gyms',
    },
  ];

  readonly loading = signal(true);

  readonly toggling = signal(false);

  ngOnInit(): void {
    this.loadCounts();
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

  private loadCounts(): void {
    this.dashboardService
      .getCounts()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
        },
        next: counts => {
          for (const item of this.cleaningItems) {
            const key = item.type === 'monsters' ? 'pokemon' : item.type;
            const count = (counts as unknown as Record<string, number>)[key] ?? 0;
            item.hasAlarms.set(count > 0);
          }
          this.loading.set(false);
        },
      });
  }
}
