import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';

import {
  ActiveHourEntry,
  ActiveHourGroup,
  compressDayRange,
  DAY_LETTERS,
  formatTime12h,
  groupActiveHours,
} from '../../../core/models/active-hours.models';

export interface ActiveHoursEditorData {
  activeHours: ActiveHourEntry[];
  profileColor?: string;
  profileName: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatSelectModule, MatTooltipModule],
  selector: 'app-active-hours-editor-dialog',
  standalone: true,
  styleUrl: './active-hours-editor-dialog.component.scss',
  templateUrl: './active-hours-editor-dialog.component.html',
})
export class ActiveHoursEditorDialogComponent {
  private readonly dialogRef = inject(MatDialogRef<ActiveHoursEditorDialogComponent>);
  readonly allDays = [1, 2, 3, 4, 5, 6, 7];

  readonly data: ActiveHoursEditorData = inject(MAT_DIALOG_DATA);
  readonly dayLetters = DAY_LETTERS;
  readonly entries = signal<ActiveHourEntry[]>([...this.data.activeHours]);
  readonly groups = computed<ActiveHourGroup[]>(() => groupActiveHours(this.entries()));
  readonly hourOptions = Array.from({ length: 24 }, (_, i) => i);
  readonly minuteOptions = Array.from({ length: 12 }, (_, i) => i * 5);
  /** Mini-preview: 7 rows x time markers */
  readonly previewData = computed(() => {
    const groups = this.groups();
    return this.allDays.map(day => ({
      day,
      markers: groups.filter(g => g.days.includes(day)).map(g => ({ hours: g.hours, mins: g.mins })),
    }));
  });

  readonly selectedDays = signal<Set<number>>(new Set<number>());
  readonly selectedHour = signal(9);

  readonly selectedMinute = signal(0);

  addEntries(): void {
    const days = this.selectedDays();
    if (days.size === 0) return;
    const h = this.selectedHour();
    const m = this.selectedMinute();
    const current = this.entries();
    const existing = new Set(current.map(e => `${e.day}:${e.hours}:${e.mins}`));
    const newEntries = [...current];
    for (const day of days) {
      const key = `${day}:${h}:${m}`;
      if (!existing.has(key)) {
        newEntries.push({ day, hours: h, mins: m });
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
    return `${compressDayRange(group.days)} ${formatTime12h(group.hours, group.mins)}`;
  }

  formatHour(h: number): string {
    const period = h >= 12 ? 'PM' : 'AM';
    const display = h % 12 || 12;
    return `${display} ${period}`;
  }

  formatMinute(m: number): string {
    return `:${m.toString().padStart(2, '0')}`;
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

  removeGroup(group: ActiveHourGroup): void {
    const daysSet = new Set(group.days);
    this.entries.set(this.entries().filter(e => !(daysSet.has(e.day) && e.hours === group.hours && e.mins === group.mins)));
  }

  save(): void {
    this.dialogRef.close(this.entries());
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
}
