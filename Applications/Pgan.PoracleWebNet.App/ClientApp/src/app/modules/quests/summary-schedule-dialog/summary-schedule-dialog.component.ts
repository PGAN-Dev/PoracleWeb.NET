import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { catchError, finalize, of } from 'rxjs';

import { ActiveHourEntry, compressDayRange, formatTime12h, groupActiveHours } from '../../../core/models/active-hours.models';
import { I18nService } from '../../../core/services/i18n.service';
import { LocationService } from '../../../core/services/location.service';
import { SettingsService } from '../../../core/services/settings.service';
import { SummarySchedule, SummaryScheduleService } from '../../../core/services/summary-schedule.service';
import {
  ActiveHoursEditorDialogComponent,
  ActiveHoursEditorData,
} from '../../../shared/components/active-hours-editor-dialog/active-hours-editor-dialog.component';
import { LocationWarningComponent } from '../../../shared/components/location-warning/location-warning.component';

export interface SummaryScheduleDialogData {
  alertType: string; // 'quest'
}

/** Client-side cooldown for the "Send summary now" trigger — purely a duplicate-delivery guard. */
const COOLDOWN_MS = 15_000;

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    LocationWarningComponent,
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    TranslatePipe,
  ],
  selector: 'app-summary-schedule-dialog',
  standalone: true,
  styleUrl: './summary-schedule-dialog.component.scss',
  templateUrl: './summary-schedule-dialog.component.html',
})
export class SummaryScheduleDialogComponent {
  /** Timestamp (ms) when the trigger cooldown expires; 0 = not cooling down. */
  private readonly cooldownUntil = signal(0);
  private readonly dialog = inject(MatDialog);

  private readonly dialogRef = inject(MatDialogRef<SummaryScheduleDialogComponent>);

  private readonly i18n = inject(I18nService);
  private readonly locationService = inject(LocationService);
  private readonly settingsService = inject(SettingsService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly summaryService = inject(SummaryScheduleService);

  readonly coolingDown = computed(() => Date.now() < this.cooldownUntil());

  readonly data: SummaryScheduleDialogData = inject(MAT_DIALOG_DATA);
  readonly schedule = signal<SummarySchedule | null>(null);
  readonly entries = computed<ActiveHourEntry[]>(() => this.schedule()?.activeHours ?? []);

  readonly hasSchedule = computed(() => this.entries().length > 0);

  readonly loading = signal(true);
  /** Grouped amber pills mirroring the active-hours-chip idiom. */
  readonly pills = computed(() =>
    groupActiveHours(this.entries()).map(g => ({ label: `${compressDayRange(g.days)} ${formatTime12h(g.hours, g.mins)}` })),
  );

  readonly saving = signal(false);

  readonly triggering = signal(false);
  readonly userLat = signal(0);

  readonly userLon = signal(0);
  /** Quests disabled: the schedule can be read and cleared, but not set or fired. */
  readonly writesDisabled = computed(() => this.settingsService.isDisabled('disable_quests'));

  constructor() {
    this.summaryService
      .getSchedule(this.data.alertType)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe(schedule => this.schedule.set(schedule));

    // 0,0 is already the default, and is what a disabled or unset location should leave in place --
    // without an error arm the same 403 threw instead. See #617.
    this.locationService
      .getLocation()
      .pipe(catchError(() => of(null)))
      .subscribe(location => {
        this.userLat.set(location?.latitude ?? 0);
        this.userLon.set(location?.longitude ?? 0);
      });
  }

  /** Remove the schedule entirely (clear all rules). */
  clearSchedule(): void {
    if (!this.hasSchedule() || this.saving()) return;
    this.saving.set(true);
    this.summaryService
      .deleteSchedule(this.data.alertType)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        error: () => this.notify('QUESTS.SUMMARY_SCHEDULE_FAILED'),
        next: () => {
          this.schedule.set({ activeHours: [], alertType: this.data.alertType });
          this.notify('QUESTS.SUMMARY_SCHEDULE_CLEARED');
        },
      });
  }

  close(): void {
    this.dialogRef.close();
  }

  /** Open the shared active-hours editor seeded with the current schedule, persist the returned array. */
  editSchedule(): void {
    const ref = this.dialog.open(ActiveHoursEditorDialogComponent, {
      maxWidth: '95vw',
      width: '560px',
      data: {
        activeHours: this.entries(),
        // Without this the editor said the profile would need a manual start, directly beneath this
        // dialog's own line about quests being delivered individually. See #457.
        emptyStateKey: 'QUESTS.SUMMARY_SCHEDULE_EMPTY',
        profileName: this.i18n.instant('QUESTS.SUMMARY_SCHEDULE_ALERT_LABEL'),
      } as ActiveHoursEditorData,
    });

    ref.afterClosed().subscribe((result: ActiveHourEntry[] | null | undefined) => {
      if (result === null || result === undefined) return;
      this.persist(result, result.length > 0 ? 'QUESTS.SUMMARY_SCHEDULE_SAVED' : 'QUESTS.SUMMARY_SCHEDULE_CLEARED');
    });
  }

  /** Flush-and-deliver the buffered quest summary now (cooldown + in-flight guarded). */
  sendNow(): void {
    if (!this.hasSchedule() || this.triggering() || this.coolingDown()) return;
    this.triggering.set(true);
    this.summaryService
      .trigger(this.data.alertType)
      .pipe(finalize(() => this.triggering.set(false)))
      .subscribe({
        error: err => this.notify(err?.status === 503 ? 'QUESTS.SUMMARY_SCHEDULE_UNAVAILABLE' : 'QUESTS.SUMMARY_SCHEDULE_FAILED'),
        next: () => {
          this.cooldownUntil.set(Date.now() + COOLDOWN_MS);
          setTimeout(() => this.cooldownUntil.set(0), COOLDOWN_MS);
          this.notify('QUESTS.SUMMARY_SCHEDULE_SENT');
        },
      });
  }

  private notify(key: string): void {
    this.snackBar.open(this.i18n.instant(key), this.i18n.instant('COMMON.OK'), { duration: 4000 });
  }

  private persist(hours: ActiveHourEntry[], successKey: string): void {
    this.saving.set(true);
    this.summaryService
      .setSchedule(this.data.alertType, hours)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        error: err => this.notify(err?.status === 503 ? 'QUESTS.SUMMARY_SCHEDULE_UNAVAILABLE' : 'QUESTS.SUMMARY_SCHEDULE_FAILED'),
        next: () => {
          this.schedule.set({ activeHours: hours, alertType: this.data.alertType });
          this.notify(successKey);
        },
      });
  }
}
