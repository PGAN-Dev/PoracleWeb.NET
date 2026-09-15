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
import { firstValueFrom } from 'rxjs';

import { PokestopEvent } from '../../core/models';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { I18nService } from '../../core/services/i18n.service';
import { PokestopEventService } from '../../core/services/pokestop-event.service';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { AlarmScope, scopeToFields } from '../../shared/utils/alarm-scope';
import { AUTO_DELETE } from '../../shared/utils/clean-flags';
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
  selector: 'app-pokestop-event-add-dialog',
  standalone: true,
  styleUrl: './pokestop-event-add-dialog.component.scss',
  templateUrl: './pokestop-event-add-dialog.component.html',
})
export class PokestopEventAddDialogComponent {
  private readonly alertDefaults = inject(AlertDefaultsService);
  /** The alarms the profile already has, so an event it already tracks cannot be picked twice. */
  private readonly existing = inject<PokestopEvent[] | null>(MAT_DIALOG_DATA, { optional: true }) ?? [];
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  private readonly pokestopEventService = inject(PokestopEventService);

  private readonly snackBar = inject(MatSnackBar);

  readonly dialogRef = inject(MatDialogRef<PokestopEventAddDialogComponent>);

  form = this.fb.group({
    autoDelete: [false],
    // One control per event, named for it, so the parity check pairs them with the edit dialog's
    // single-select.
    eventGoldStop: [false],
    eventKecleon: [false],
    eventShowcase: [false],
    template: [''],
  });

  /**
   * One checkbox per event, fanning out into one alarm each — the same shape as the invasion add
   * dialog, and the reason this is multi-select rather than a dropdown. There is deliberately no
   * "any event" option: `display_type` is required upstream and has no wildcard.
   */
  readonly options = POKESTOP_EVENTS.map(e => ({
    ...e,
    alreadyTracked: this.existing.some(x => x.displayType === e.displayType),
  }));

  readonly saving = signal(false);

  /**
   * Seeded from the saved defaults so the Alert Defaults preference still reaches new alarms; the
   * picker owns it from there.
   */
  readonly scope = signal<AlarmScope>(
    this.alertDefaults.defaultMode() === 'areas'
      ? { mode: 'profile' }
      : {
          distanceKm: this.alertDefaults.defaultDistanceKm(),
          mode: this.alertDefaults.defaultPlaceLabel() ? 'place' : 'profile',
          placeLabel: this.alertDefaults.defaultPlaceLabel(),
        },
  );

  constructor() {
    // Disabling has to happen on the control, not with [disabled] in the template. A reactive form
    // control drives the checkbox through its value accessor, and the accessor's setDisabledState --
    // called with the control's own enabled state -- runs after the input binding and puts the box
    // back. The box stayed tickable, and ticking it did nothing: chosen() filters alreadyTracked out,
    // so Save stayed disabled with nothing on screen saying why.
    for (const option of this.options.filter(o => o.alreadyTracked)) {
      this.form.controls[this.controlFor(option.name)].disable();
    }
  }

  controlFor(name: string): 'eventGoldStop' | 'eventKecleon' | 'eventShowcase' {
    return name === 'kecleon' ? 'eventKecleon' : name === 'gold-stop' ? 'eventGoldStop' : 'eventShowcase';
  }

  hasSelection(): boolean {
    return this.chosen().length > 0;
  }

  async save(): Promise<void> {
    const chosen = this.chosen();
    if (chosen.length === 0) return;

    this.saving.set(true);
    const v = this.form.getRawValue();
    const scope = scopeToFields(this.scope());

    // One request per event. The API refuses a second alarm for an event the profile already tracks,
    // so a partial success is possible and each is reported on its own rather than as one verdict.
    let created = 0;
    for (const displayType of chosen) {
      try {
        await firstValueFrom(
          this.pokestopEventService.create({
            overrideAreas: scope.overrideAreas,
            overrideLocationLabel: scope.overrideLocationLabel,
            clean: v.autoDelete ? AUTO_DELETE : 0,
            displayType,
            distance: scope.distance,
            template: v.template || null,
          }),
        );
        created++;
      } catch (err) {
        const message = (err as { error?: { error?: string } })?.error?.error;
        this.snackBar.open(message ?? this.i18n.instant('POKESTOP_EVENTS.CREATE_FAILED'), this.i18n.instant('COMMON.OK'), {
          duration: 6000,
        });
        this.saving.set(false);
        // Whatever was written before the failure still exists, so close and reload rather than
        // leaving the list showing a state that is already out of date.
        this.dialogRef.close(created > 0);
        return;
      }
    }

    this.snackBar.open(this.i18n.instant('POKESTOP_EVENTS.CREATE_SUCCESS'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
    this.dialogRef.close(true);
  }

  private chosen(): number[] {
    const v = this.form.getRawValue();
    return this.options.filter(o => !o.alreadyTracked && v[this.controlFor(o.name)]).map(o => o.displayType);
  }
}
