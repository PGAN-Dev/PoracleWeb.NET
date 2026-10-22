import { inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { CanDeactivateFn } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { from, map, switchMap } from 'rxjs';

import type { ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';

/** A page that stages edits behind a Save button, and can say whether any are still unsaved. */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * Asks before an in-app navigation drops staged edits. Admin > Areas and Admin > Settings both stage
 * changes behind a save bar, and a click on the sidenav used to throw them away without a word.
 *
 * A clean page, and one just saved, leaves without a prompt: the question is only asked when the page
 * itself reports something unsaved. A browser reload or tab close is the page's own `beforeunload`.
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = component => {
  if (!component?.hasUnsavedChanges?.()) return true;

  const i18n = inject(TranslateService);
  const data: ConfirmDialogData = {
    cancelText: i18n.instant('COMMON.UNSAVED_STAY'),
    confirmText: i18n.instant('COMMON.UNSAVED_LEAVE'),
    message: i18n.instant('COMMON.UNSAVED_LEAVE_MESSAGE'),
    title: i18n.instant('COMMON.UNSAVED_LEAVE_TITLE'),
    warn: true,
  };
  const dialog = inject(MatDialog);
  // Loaded on demand: the routes file is in the initial bundle, and the dialog is only needed once
  // somebody actually leaves with something unsaved.
  return from(import('../../shared/components/confirm-dialog/confirm-dialog.component')).pipe(
    switchMap(({ ConfirmDialogComponent }) => dialog.open(ConfirmDialogComponent, { data }).afterClosed()),
    map(confirmed => confirmed === true),
  );
};
