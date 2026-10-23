import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslatePipe } from '@ngx-translate/core';

import { FortChange, FortChangeUpdate, LinkSide } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { FortChangeService } from '../../core/services/fort-change.service';
import { I18nService } from '../../core/services/i18n.service';
import { SettingsService } from '../../core/services/settings.service';
import { DeliverToFieldComponent } from '../../shared/components/deliver-to/deliver-to-field.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { createFailureText } from '../../shared/utils/add-outcome';
import { AlarmScope, metresToKm, scopeOf, scopeToFields } from '../../shared/utils/alarm-scope';
import { injectDeliverTo, moveEditMessage } from '../../shared/utils/deliver-to';
import { mentionCapable } from '../../shared/utils/human-kind';

/** The change types this dialog has a checkbox for. Anything else is carried through untouched. */
const KNOWN_CHANGE_TYPES = new Set(['name', 'location', 'image_url', 'removal', 'new', 'description']);

@Component({
  imports: [
    DeliverToFieldComponent,
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatRadioModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatTabsModule,
    MatSnackBarModule,
    TranslatePipe,
    TemplateSelectorComponent,
    ScopePickerComponent,
  ],
  selector: 'app-fort-change-edit-dialog',
  standalone: true,
  styleUrl: './fort-change-edit-dialog.component.scss',
  templateUrl: './fort-change-edit-dialog.component.html',
})
export class FortChangeEditDialogComponent {
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly fortChangeService = inject(FortChangeService);
  private readonly i18n = inject(I18nService);
  private readonly settings = inject(SettingsService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<FortChange>(MAT_DIALOG_DATA);
  readonly deliver = injectDeliverTo();

  readonly dialogRef = inject(MatDialogRef<FortChangeEditDialogComponent>);

  form = this.fb.group({
    changeTypeDescription: [this.data.changeTypes?.includes('description') ?? false],
    changeTypeImageUrl: [this.data.changeTypes?.includes('image_url') ?? false],
    changeTypeLocation: [this.data.changeTypes?.includes('location') ?? false],
    changeTypeName: [this.data.changeTypes?.includes('name') ?? false],
    changeTypeNew: [this.data.changeTypes?.includes('new') ?? false],
    changeTypeRemoval: [this.data.changeTypes?.includes('removal') ?? false],
    destination: [(this.data.destination ?? null) as LinkSide | null],
    fortType: [this.data.fortType ?? 'everything'],
    includeEmpty: [this.data.includeEmpty === 1],
    // Always present even when the Mention control is hidden (not a webhook alarm) -- so a mention
    // round-trips unchanged through a save this dialog makes for an unrelated field.
    ping: [this.data.ping ?? '', [Validators.maxLength(255)]],
    template: [this.data.template ?? ''],
  });

  /** False while the scope picker holds a radius it won't accept, which keeps Save off. */
  readonly radiusValid = signal(true);

  saving = signal(false);

  /** The alarm's current scope, read back into the shared picker. */
  readonly scope = signal<AlarmScope>(scopeOf(this.data.overrideLocationLabel, this.data.overrideAreas, this.data.distance));

  readonly showMention = this.auth.isImpersonating() && mentionCapable(this.auth.user()?.type);

  /** The radius as stored, which an edit may keep even outside the picker's range. */
  readonly storedRadiusKm = metresToKm(this.data.distance);

  /**
   * Whether the template selector shows. Fort changes have no clean bit, so with templates off the
   * "Message settings" heading would label nothing (the Mention field aside, on a webhook edit).
   */
  readonly templatesEnabled = computed(() => this.settings.siteSettings()['enable_templates']?.toLowerCase() === 'true');

  save(): void {
    this.saving.set(true);
    const v = this.form.getRawValue();
    const scope = scopeToFields(this.scope());
    const changeTypes: string[] = [];
    if (v.changeTypeName) changeTypes.push('name');
    if (v.changeTypeLocation) changeTypes.push('location');
    if (v.changeTypeImageUrl) changeTypes.push('image_url');
    if (v.changeTypeRemoval) changeTypes.push('removal');
    if (v.changeTypeNew) changeTypes.push('new');
    if (v.changeTypeDescription) changeTypes.push('description');

    // Anything stored that this dialog has no box for stays. The list is rebuilt from the boxes, so a
    // change type PoracleNG grows before PoracleWeb does would be dropped by the next save someone
    // makes for an unrelated reason.
    for (const stored of this.data.changeTypes ?? []) {
      if (!KNOWN_CHANGE_TYPES.has(stored) && !changeTypes.includes(stored)) changeTypes.push(stored);
    }

    this.fortChangeService
      .update(this.data.uid, {
        ...this.deliver.moveFields(this.data.destination, this.form.controls.destination.value),
        overrideAreas: scope.overrideAreas,
        overrideLocationLabel: scope.overrideLocationLabel,
        changeTypes,
        distance: scope.distance,
        fortType: v.fortType,
        includeEmpty: v.includeEmpty ? 1 : 0,
        ping: v.ping ?? '',
        template: v.template || '',
      } as FortChangeUpdate)
      .subscribe({
        // The server names what is wrong -- which alarm already uses these settings, which
        // field a file got wrong. A fixed string threw that away. See #567, #568.
        error: (err: unknown) => {
          this.snackBar.open(createFailureText(err, this.i18n, 'ALARM.FAILED_UPDATE'), this.i18n.instant('COMMON.OK'), {
            duration: 6000,
          });
          this.saving.set(false);
        },
        next: moved => {
          this.snackBar.open(
            moveEditMessage(moved, this.i18n) ?? this.i18n.instant('FORT_CHANGES.UPDATE_SUCCESS'),
            this.i18n.instant('COMMON.OK'),
            { duration: 3000 },
          );
          this.dialogRef.close(true);
        },
      });
  }
}
