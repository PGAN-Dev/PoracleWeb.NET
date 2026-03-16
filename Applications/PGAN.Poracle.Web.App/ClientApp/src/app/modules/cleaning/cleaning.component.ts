import { Component, OnInit, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CleaningService, CleanAlarmType } from '../../core/services/cleaning.service';
import { DashboardService } from '../../core/services/dashboard.service';

interface CleaningItem {
  type: CleanAlarmType;
  label: string;
  icon: string;
  color: string;
  enabled: ReturnType<typeof signal<boolean>>;
  hasAlarms: ReturnType<typeof signal<boolean>>;
}

@Component({
  selector: 'app-cleaning',
  standalone: true,
  imports: [
    MatCardModule,
    MatSlideToggleModule,
    MatIconModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
  ],
  template: `
    <div class="cleaning-container">
      <div class="page-header">
        <div class="page-header-text">
          <h1>Cleaning Settings</h1>
          <p class="page-description">Enable auto-cleanup to automatically remove expired notification alarms. When enabled, alarms that have been triggered will be automatically cleaned up.</p>
        </div>
      </div>

      @if (loading()) {
        <div class="loading-container">
          <mat-spinner diameter="48"></mat-spinner>
        </div>
      } @else {
        <mat-card class="cleaning-card">
          @for (item of cleaningItems; track item.type) {
            <div class="cleaning-row">
              <div class="cleaning-label">
                <mat-icon [style.color]="item.color">{{ item.icon }}</mat-icon>
                <div class="label-text">
                  <span class="label-name">{{ item.label }}</span>
                  @if (!item.hasAlarms()) {
                    <span class="label-hint">No alarms configured</span>
                  }
                </div>
              </div>
              <mat-slide-toggle
                [checked]="item.enabled()"
                [disabled]="toggling()"
                (change)="toggleClean(item, $event.checked)">
              </mat-slide-toggle>
            </div>
          }
        </mat-card>
      }
    </div>
  `,
  styles: [
    `
      .cleaning-container {
        padding: 0 24px 24px;
        max-width: 600px;
        margin: 0 auto;
      }
      .page-header {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        padding: 16px 0;
        gap: 16px;
      }
      .page-header-text {
        flex: 1;
        min-width: 0;
      }
      .page-header h1 {
        margin: 0;
        font-size: 24px;
        font-weight: 400;
      }
      .page-description {
        margin: 4px 0 0;
        color: var(--text-secondary, rgba(0,0,0,0.54));
        font-size: 13px;
        line-height: 1.5;
        border-left: 3px solid #1976d2;
        padding-left: 12px;
      }
      .loading-container {
        display: flex;
        justify-content: center;
        padding: 64px;
      }
      .cleaning-card {
        padding: 0;
      }
      .cleaning-row {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 16px 24px;
        border-bottom: 1px solid var(--divider, rgba(0, 0, 0, 0.08));
      }
      .cleaning-row:last-child {
        border-bottom: none;
      }
      .cleaning-label {
        display: flex;
        align-items: center;
        gap: 16px;
      }
      .label-text {
        display: flex;
        flex-direction: column;
      }
      .label-name {
        font-size: 16px;
      }
      .label-hint {
        font-size: 12px;
        color: var(--text-hint, rgba(0, 0, 0, 0.38));
      }
    `,
  ],
})
export class CleaningComponent implements OnInit {
  private readonly cleaningService = inject(CleaningService);
  private readonly dashboardService = inject(DashboardService);
  private readonly snackBar = inject(MatSnackBar);

  readonly loading = signal(true);
  readonly toggling = signal(false);

  readonly cleaningItems: CleaningItem[] = [
    {
      type: 'monsters',
      label: 'Pokemon Alarms',
      icon: 'catching_pokemon',
      color: '#4CAF50',
      enabled: signal(false),
      hasAlarms: signal(false),
    },
    {
      type: 'raids',
      label: 'Raids & Eggs',
      icon: 'shield',
      color: '#F44336',
      enabled: signal(false),
      hasAlarms: signal(false),
    },
    {
      type: 'quests',
      label: 'Quests',
      icon: 'explore',
      color: '#9C27B0',
      enabled: signal(false),
      hasAlarms: signal(false),
    },
    {
      type: 'invasions',
      label: 'Invasions',
      icon: 'warning',
      color: '#607D8B',
      enabled: signal(false),
      hasAlarms: signal(false),
    },
    {
      type: 'lures',
      label: 'Lures',
      icon: 'location_on',
      color: '#E91E63',
      enabled: signal(false),
      hasAlarms: signal(false),
    },
    {
      type: 'nests',
      label: 'Nests',
      icon: 'park',
      color: '#8BC34A',
      enabled: signal(false),
      hasAlarms: signal(false),
    },
    {
      type: 'gyms',
      label: 'Gyms',
      icon: 'fitness_center',
      color: '#00BCD4',
      enabled: signal(false),
      hasAlarms: signal(false),
    },
  ];

  ngOnInit(): void {
    this.loadCounts();
  }

  private loadCounts(): void {
    this.dashboardService.getCounts().subscribe({
      next: (counts) => {
        for (const item of this.cleaningItems) {
          const key = item.type === 'monsters' ? 'pokemon' : item.type;
          const count = (counts as unknown as Record<string, number>)[key] ?? 0;
          item.hasAlarms.set(count > 0);
        }
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      },
    });
  }

  toggleClean(item: CleaningItem, enabled: boolean): void {
    this.toggling.set(true);
    this.cleaningService.toggleClean(item.type, enabled).subscribe({
      next: (result) => {
        item.enabled.set(enabled);
        this.toggling.set(false);
        const action = enabled ? 'enabled' : 'disabled';
        this.snackBar.open(
          `Cleaning ${action} for ${item.label} (${result.updated} alarms updated)`,
          'OK',
          { duration: 3000 },
        );
      },
      error: () => {
        this.toggling.set(false);
        this.snackBar.open(`Failed to update cleaning for ${item.label}`, 'OK', {
          duration: 3000,
        });
      },
    });
  }
}
