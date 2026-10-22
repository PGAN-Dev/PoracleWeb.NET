import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';

import { hasRefusedNameCharacter } from '../../utils/place-label';

export interface ConfirmDialogData {
  cancelText?: string;
  confirmText?: string;
  /** One button (`confirmText`, else Close) that closes with false: the dialog explains, it asks nothing. */
  infoOnly?: boolean;
  itemDescription?: string;
  /** A short list under the message, such as the alarms that stop a delete. */
  items?: string[];
  message: string;
  promptField?: ConfirmDialogPrompt;
  showDontAskAgain?: boolean;
  title: string;
  warn?: boolean;
}

/** A one-line name prompt. Save stays off while the trimmed value is empty or breaks one of these rules. */
export interface ConfirmDialogPrompt {
  /** Translation key shown for a duplicate. Defaults to the profile wording, `DIALOG.PROMPT_CONFLICT`. */
  conflictKey?: string;
  /** Names already taken, compared trimmed and case-insensitively. */
  existingNames?: string[];
  label: string;
  /** Caps the input and shows a counter. */
  maxLength?: number;
  /** Refuses control, line-break and text-direction characters, the rule the API applies to the name. */
  refuseHiddenCharacters?: boolean;
  value: string;
}

export interface ConfirmDialogResult {
  confirmed: boolean;
  dontAskAgain: boolean;
}

@Component({
  // An alertdialog needs a name as well as a description; without aria-labelledby a screen reader
  // announced "alert dialog" and went straight to the body (axe aria-dialog-name, serious).
  host: {
    'aria-describedby': 'confirm-dialog-message',
    'aria-labelledby': 'confirm-dialog-title',
    role: 'alertdialog',
  },
  imports: [
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    TranslatePipe,
  ],
  selector: 'app-confirm-dialog',
  standalone: true,
  styleUrl: './confirm-dialog.component.scss',
  templateUrl: './confirm-dialog.component.html',
})
export class ConfirmDialogComponent {
  readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<ConfirmDialogComponent>);

  dontAskAgain = false;
  /** Shows the prompt's error as soon as it is wrong, rather than waiting for the field to lose focus. */
  readonly promptErrorMatcher: ErrorStateMatcher = { isErrorState: () => this.promptProblemShown !== null };
  /** Set once the user has typed, so an empty prompt is not called out before they start. */
  promptTouched = false;
  promptValue = this.data.promptField?.value ?? '';

  get hasPromptConflict(): boolean {
    if (!this.data.promptField?.existingNames) return false;
    const val = this.promptValue.trim().toLowerCase();
    return this.data.promptField.existingNames.some(n => n.trim().toLowerCase() === val);
  }

  /** The translation key for what is wrong with the prompt's value, or null when it can be saved. */
  get promptProblem(): null | string {
    const prompt = this.data.promptField;
    if (!prompt) return null;
    const value = this.promptValue.trim();
    if (!value) return 'DIALOG.PROMPT_REQUIRED';
    if (prompt.maxLength && value.length > prompt.maxLength) return 'DIALOG.PROMPT_TOO_LONG';
    if (prompt.refuseHiddenCharacters && hasRefusedNameCharacter(value)) return 'DIALOG.PROMPT_HIDDEN_CHARACTERS';
    if (this.hasPromptConflict) return prompt.conflictKey ?? 'DIALOG.PROMPT_CONFLICT';
    return null;
  }

  /** The problem to show under the field: every one at once, except "required" before the user has typed. */
  get promptProblemShown(): null | string {
    const problem = this.promptProblem;
    return problem === 'DIALOG.PROMPT_REQUIRED' && !this.promptTouched ? null : problem;
  }

  onCancel(): void {
    if (this.data.showDontAskAgain) {
      this.dialogRef.close({ confirmed: false, dontAskAgain: false } as ConfirmDialogResult);
    } else {
      this.dialogRef.close(false);
    }
  }

  onConfirm(): void {
    if (this.data.promptField) {
      this.dialogRef.close(this.promptValue.trim() || false);
    } else if (this.data.showDontAskAgain) {
      this.dialogRef.close({ confirmed: true, dontAskAgain: this.dontAskAgain } as ConfirmDialogResult);
    } else {
      this.dialogRef.close(true);
    }
  }
}
