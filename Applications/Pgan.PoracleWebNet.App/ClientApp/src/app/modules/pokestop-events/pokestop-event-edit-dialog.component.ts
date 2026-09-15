import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslatePipe } from '@ngx-translate/core';

import { PokestopEvent, PokestopEventUpdate } from '../../core/models';
import { I18nService } from '../../core/services/i18n.service';
import { PokestopEventService } from '../../core/services/pokestop-event.service';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { AlarmScope, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { AUTO_DELETE, isAutoDelete, preserve } from '../../shared/utils/clean-flags';
import { POKESTOP_EVENTS } from '../../shared/utils/pokestop-events';

@Component({
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatTabsModule,
    MatSnackBarModule,
    TranslatePipe,
    TemplateSelectorComponent,
    ScopePickerComponent,
  ],
  selector: 'app-pokestop-event-edit-dialog',
  standalone: true,
  styleUrl: './pokestop-event-edit-dialog.component.scss',
  templateUrl: './pokestop-event-edit-dialog.component.html',
})
export class PokestopEventEditDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly pokestopEventService = inject(PokestopEventService);
  private readonly snackBar = inject(MatSnackBar);

  readonly data = inject<PokestopEvent>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<PokestopEventEditDialogComponent>);
  form = this.fb.group({
    autoDelete: [isAutoDelete(this.data.clean)],
    displayType: [this.data.displayType],
    template: [this.data.template ?? ''],
  });

  readonly options = POKESTOP_EVENTS;

  readonly saving = signal(false);

  /** The alarm's current scope, read back into the shared picker. */
  readonly scope = signal<AlarmScope>(scopeOf(this.data.overrideLocationLabel, this.data.overrideAreas, this.data.distance));

  save(): void {
    this.saving.set(true);
    const v = this.form.getRawValue();
    const scope = scopeToFields(this.scope());

    this.pokestopEventService
      .update(this.data.uid, {
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        // Only the auto-delete bit is this dialog's to set; edit-in-place and summary bits set
        // elsewhere survive. See #292.
        clean: preserve(this.data.clean, AUTO_DELETE, v.autoDelete ? AUTO_DELETE : 0),
        displayType: v.displayType ?? this.data.displayType,
        distance: scope.distance,
        template: v.template || '',
      } as PokestopEventUpdate)
      .subscribe({
        // The server names what is in the way -- which alarm already holds that event, or which
        // event this Poracle has no game data for. A fixed string would throw that away.
        error: (err: { error?: { error?: string } }) => {
          this.snackBar.open(err?.error?.error ?? this.i18n.instant('POKESTOP_EVENTS.UPDATE_FAILED'), this.i18n.instant('COMMON.OK'), {
            duration: 6000,
          });
          this.saving.set(false);
        },
        next: () => {
          this.snackBar.open(this.i18n.instant('POKESTOP_EVENTS.UPDATE_SUCCESS'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
          this.dialogRef.close(true);
        },
      });
  }
}
