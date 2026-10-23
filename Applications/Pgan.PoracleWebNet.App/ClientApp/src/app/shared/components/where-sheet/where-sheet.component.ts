import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogConfig, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { TranslatePipe } from '@ngx-translate/core';

import { AlarmScope } from '../../utils/alarm-scope';
import { ScopePickerComponent } from '../scope-picker/scope-picker.component';

export interface WhereSheetData {
  /** Areas the profile subscribes to, so the inherited option can say what it means. */
  profileAreas: string[];
  scope: AlarmScope;
}

/**
 * Panel class for a dialog that a phone shows as a bottom sheet sized to its content, rather than the
 * full-screen treatment every other dialog gets there (styles.scss, "Phone bottom sheet").
 */
export const PHONE_BOTTOM_SHEET = 'phone-bottom-sheet';

/**
 * How every list opens the sheet. On a phone it rises from the bottom at its own height, so the card
 * being changed stays in view above it; full screen spent the whole display on three radio rows.
 */
export function whereSheetConfig(data: WhereSheetData): MatDialogConfig<WhereSheetData> {
  return { width: '520px', autoFocus: false, data, panelClass: PHONE_BOTTOM_SHEET };
}

/**
 * Changing an alarm's scope from its card, where there is no form to put the control in.
 *
 * A shell around ScopePickerComponent and nothing else. The alarm dialogs render the same component
 * inline; when this held its own copy of the control the two drifted apart within a day, and the
 * dialog copy was missing an option entirely.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, ScopePickerComponent, TranslatePipe],
  selector: 'app-where-sheet',
  standalone: true,
  styleUrl: './where-sheet.component.scss',
  templateUrl: './where-sheet.component.html',
})
export class WhereSheetComponent {
  readonly data = inject<WhereSheetData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject<MatDialogRef<WhereSheetComponent, AlarmScope>>(MatDialogRef);

  /** False while the picker holds a radius it won't accept, which keeps Save off. */
  readonly radiusValid = signal(true);

  readonly scope = signal<AlarmScope>(this.data.scope);

  /** The radius the alarm has now, which the sheet may keep even outside the picker's range. */
  readonly storedRadiusKm = this.data.scope.mode === 'areas' ? null : (this.data.scope.distanceKm ?? null);

  save(): void {
    if (!this.radiusValid()) return;
    this.dialogRef.close(this.scope());
  }
}
