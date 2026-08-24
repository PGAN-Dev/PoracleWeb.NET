import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { TranslatePipe } from '@ngx-translate/core';

import { MuteScope, MuteService, QUIET_DURATIONS } from '../../../core/services/mute.service';

export interface QuietSheetData {
  /** Minutes already selected, when the subject is quiet and this is a re-quiet. */
  currentMinutes?: number;
  /** True when the subject is quiet now, which adds the way back out. */
  isQuiet: boolean;
  scope: MuteScope;
  /** The subject in the user's words: a gym name, a species, an area. Not an id. */
  subject: string;
  value: string;
}

/** What the sheet was closed with: a duration in minutes, or a request to lift the quiet period. */
export type QuietSheetResult = { minutes: number; type: 'quiet' } | { type: 'resume' };

/**
 * Choosing how long to quiet something for.
 *
 * A shell around one control, in the shape of WhereSheetComponent. Six durations rather than a minutes
 * box: the ceiling is a week and nobody types 10080.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatChipsModule, MatDialogModule, TranslatePipe],
  selector: 'app-quiet-sheet',
  standalone: true,
  styleUrl: './quiet-sheet.component.scss',
  templateUrl: './quiet-sheet.component.html',
})
export class QuietSheetComponent {
  private readonly mutes = inject(MuteService);
  readonly data = inject<QuietSheetData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject<MatDialogRef<QuietSheetComponent, QuietSheetResult>>(MatDialogRef);

  readonly durations = QUIET_DURATIONS;
  readonly minutes = signal(this.data.currentMinutes ?? 60);

  confirm(): void {
    this.dialogRef.close({ minutes: this.minutes(), type: 'quiet' });
  }

  label(minutes: number): string {
    return this.mutes.durationLabel(minutes);
  }

  resume(): void {
    this.dialogRef.close({ type: 'resume' });
  }
}
