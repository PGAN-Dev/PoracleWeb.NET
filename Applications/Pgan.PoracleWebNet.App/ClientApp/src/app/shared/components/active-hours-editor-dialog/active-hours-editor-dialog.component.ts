import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

import {
  ActiveHourEntry,
  ActiveHourGroup,
  activeHoursFires,
  DAY_LETTERS,
  formatRuleLabel,
  groupActiveHours,
} from '../../../core/models/active-hours.models';

export interface ActiveHoursEditorData {
  activeHours: ActiveHourEntry[];
  /**
   * Translation key for the line shown when there are no rules. The editor serves two contexts that mean
   * different things by a schedule: profile rules drive PoracleNG's profile scheduler, quest rules drive
   * summary delivery. The default keeps the profile wording, so profile callers need no change. See #457.
   */
  emptyStateKey?: string;
  profileColor?: string;
  profileName: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatTooltipModule,
    TranslatePipe,
  ],
  selector: 'app-active-hours-editor-dialog',
  standalone: true,
  styleUrl: './active-hours-editor-dialog.component.scss',
  templateUrl: './active-hours-editor-dialog.component.html',
})
export class ActiveHoursEditorDialogComponent {
  private readonly dialogRef = inject(MatDialogRef<ActiveHoursEditorDialogComponent>);
  private readonly translate = inject(TranslateService);
  readonly allDays = [1, 2, 3, 4, 5, 6, 7];

  readonly data: ActiveHoursEditorData = inject(MAT_DIALOG_DATA);
  readonly dayLetters = DAY_LETTERS;
  readonly entries = signal<ActiveHourEntry[]>([...this.data.activeHours]);
  readonly groups = computed<ActiveHourGroup[]>(() => groupActiveHours(this.entries()));
  readonly hourOptions = Array.from({ length: 24 }, (_, i) => i);
  readonly minuteOptions = Array.from({ length: 12 }, (_, i) => i * 5);

  /**
   * Mini-preview: 7 rows, each carrying its rules' fire dots and, for a range, the span they sit
   * inside. Past a dozen fires the dots smear into each other on an 8px bar, so the span shows alone.
   */
  readonly previewData = computed(() => {
    const groups = this.groups();
    return this.allDays.map(day => ({
      day,
      rules: groups
        .filter(g => g.days.includes(day))
        .map(g => {
          const fires = activeHoursFires(g);
          return {
            end: (g.endHours ?? 0) * 60 + (g.endMins ?? 0),
            isRange: g.step > 0,
            key: this.groupKey(g),
            markers: fires.length > 12 ? [] : fires.map(([hours, mins]) => ({ hours, mins })),
            start: g.hours * 60 + g.mins,
          };
        }),
    }));
  });

  readonly repeatEnabled = signal(false);
  readonly selectedEndHour = signal(17);
  readonly selectedEndMinute = signal(0);
  readonly selectedHour = signal(9);
  readonly selectedMinute = signal(0);
  /**
   * The end has to be strictly after the start. That is the rule PoracleNG's own settime parser
   * enforces, and it rejects end-before-start and cross-midnight with the same error, so one
   * message covers both here too.
   */
  readonly rangeInvalid = computed(
    () =>
      this.repeatEnabled() && this.selectedEndHour() * 60 + this.selectedEndMinute() <= this.selectedHour() * 60 + this.selectedMinute(),
  );

  readonly selectedDays = signal<Set<number>>(new Set<number>());

  readonly selectedStep = signal(1);

  readonly stepOptions = Array.from({ length: 23 }, (_, i) => i + 1);

  addEntries(): void {
    const days = this.selectedDays();
    if (days.size === 0 || this.rangeInvalid()) return;
    const h = this.selectedHour();
    const m = this.selectedMinute();
    if (h < 0 || h > 23 || m < 0 || m > 59) return;
    const repeat = this.repeatEnabled();
    const current = this.entries();
    const existing = new Set(current.map(e => this.entryKey(e)));
    const newEntries = [...current];
    for (const day of days) {
      const entry: ActiveHourEntry = repeat
        ? { day, endHours: this.selectedEndHour(), endMins: this.selectedEndMinute(), hours: h, mins: m, step: this.selectedStep() }
        : { day, hours: h, mins: m };
      if (!existing.has(this.entryKey(entry))) {
        newEntries.push(entry);
      }
    }
    this.entries.set(newEntries);
  }

  cancel(): void {
    this.dialogRef.close(undefined);
  }

  clearAll(): void {
    this.entries.set([]);
  }

  formatGroupLabel(group: ActiveHourGroup): string {
    return formatRuleLabel(group, (key, params) => this.translate.instant(key, params));
  }

  formatHour(h: number): string {
    const period = h >= 12 ? 'PM' : 'AM';
    const display = h % 12 || 12;
    return `${display} ${period}`;
  }

  formatMinute(m: number): string {
    return `:${m.toString().padStart(2, '0')}`;
  }

  formatStep(step: number): string {
    return this.translate.instant(step === 1 ? 'PROFILES.ACTIVE_HOURS_HOURS_ONE' : 'PROFILES.ACTIVE_HOURS_HOURS_OTHER', { step });
  }

  /** Stable identity for a rules-list row, so two rules can never collide on a translated string. */
  groupKey(group: ActiveHourGroup): string {
    return `${group.days.join(',')}:${group.hours}:${group.mins}:${group.endHours ?? ''}:${group.endMins ?? ''}:${group.step}`;
  }

  isRange(group: ActiveHourGroup): boolean {
    return group.step > 0;
  }

  markerLeft(hours: number, mins: number): string {
    const totalMins = hours * 60 + mins;
    return `${(totalMins / 1440) * 100}%`;
  }

  presetDays(preset: 'weekdays' | 'weekends' | 'everyday'): void {
    switch (preset) {
      case 'weekdays':
        this.selectedDays.set(new Set([1, 2, 3, 4, 5]));
        break;
      case 'weekends':
        this.selectedDays.set(new Set([6, 7]));
        break;
      case 'everyday':
        this.selectedDays.set(new Set([1, 2, 3, 4, 5, 6, 7]));
        break;
    }
  }

  /**
   * Removes only the rule that was clicked. Comparing the end and step matters: without it, deleting
   * a 9:00 single fire would also delete a 9:00-5:00 PM/2 range that happens to share its start.
   */
  removeGroup(group: ActiveHourGroup): void {
    const daysSet = new Set(group.days);
    this.entries.set(
      this.entries().filter(
        e =>
          !(
            daysSet.has(e.day) &&
            e.hours === group.hours &&
            e.mins === group.mins &&
            (e.step ?? 0) === group.step &&
            ((e.step ?? 0) === 0 || ((e.endHours ?? 0) === (group.endHours ?? 0) && (e.endMins ?? 0) === (group.endMins ?? 0)))
          ),
      ),
    );
  }

  save(): void {
    this.dialogRef.close(this.entries());
  }

  spanLeft(startMins: number): string {
    return `${(startMins / 1440) * 100}%`;
  }

  spanWidth(startMins: number, endMins: number): string {
    return `${(Math.max(endMins - startMins, 0) / 1440) * 100}%`;
  }

  toggleDay(day: number): void {
    const current = new Set(this.selectedDays());
    if (current.has(day)) {
      current.delete(day);
    } else {
      current.add(day);
    }
    this.selectedDays.set(current);
  }

  private entryKey(e: ActiveHourEntry): string {
    return `${e.day}:${e.hours}:${e.mins}:${e.endHours ?? ''}:${e.endMins ?? ''}:${e.step ?? 0}`;
  }
}
