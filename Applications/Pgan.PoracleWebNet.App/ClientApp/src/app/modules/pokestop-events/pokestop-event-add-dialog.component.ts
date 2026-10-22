import { Component, computed, effect, inject, signal } from '@angular/core';
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
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { PokestopEvent } from '../../core/models';
import { AlertDefaultsService } from '../../core/services/alert-defaults.service';
import { I18nService } from '../../core/services/i18n.service';
import { PokestopEventService } from '../../core/services/pokestop-event.service';
import { AddRefusalComponent } from '../../shared/components/add-refusal/add-refusal.component';
import { ScopePickerComponent } from '../../shared/components/scope-picker/scope-picker.component';
import { TemplateSelectorComponent } from '../../shared/components/template-selector/template-selector.component';
import { AddRefusal, editExistingResult, ExistingRule, refusalOf, reportCreated, ruleSummary } from '../../shared/utils/add-outcome';
import { AlarmScope, scopeToFields } from '../../shared/utils/alarm-scope';
import { AUTO_DELETE } from '../../shared/utils/clean-flags';
import { POKESTOP_EVENTS } from '../../shared/utils/pokestop-events';
import { serverReason } from '../../shared/utils/server-reason';

/** What the list passes when it opens the dialog. */
export interface PokestopEventAddDialogData {
  /** The alarms the profile already has, so an event it already tracks cannot be picked twice. */
  events: PokestopEvent[];
  /** The profile's areas, so a clashing rule with no scope of its own is summarised as the list shows it. */
  profileAreas: string[];
}

@Component({
  imports: [
    AddRefusalComponent,
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
  private readonly data = inject<null | PokestopEventAddDialogData>(MAT_DIALOG_DATA, { optional: true });
  /** The alarms the profile already has, so an event it already tracks cannot be picked twice. */
  private readonly existing = this.data?.events ?? [];
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
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

  /**
   * One checkbox per event, fanning out into one alarm each — the same shape as the invasion add
   * dialog, and the reason this is multi-select rather than a dropdown. There is deliberately no
   * "any event" option: `display_type` is required upstream and has no wildcard.
   *
   * An event counts as tracked only when a stored rule for it has the scope chosen below. PoracleNG
   * stores the same event under a different area scope, place or radius as a separate rule, so
   * blocking the event outright refused an Add the server would take.
   */
  private readonly optionsState = computed(() => {
    const scope = scopeToFields(this.scope());
    return POKESTOP_EVENTS.map(e => ({
      ...e,
      alreadyTracked: this.existing.some(x => x.displayType === e.displayType && sameScope(x, scope)),
    }));
  });

  private readonly pokestopEventService = inject(PokestopEventService);

  private readonly snackBar = inject(MatSnackBar);

  private readonly translate = inject(TranslateService);

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

  /** False while the scope picker holds a radius it won't accept, which keeps Save off. */
  readonly radiusValid = signal(true);

  /** Why the last single Add was refused; shown above the actions, cleared on the next save. */
  readonly refusal = signal<AddRefusal | null>(null);

  readonly saving = signal(false);

  constructor() {
    // Disabling has to happen on the control, not with [disabled] in the template. A reactive form
    // control drives the checkbox through its value accessor, and the accessor's setDisabledState --
    // called with the control's own enabled state -- runs after the input binding and puts the box
    // back. The box stayed tickable, and ticking it did nothing: chosen() filters alreadyTracked out,
    // so Save stayed disabled with nothing on screen saying why.
    // The scope can change under the boxes, so the disabled state follows it.
    effect(() => {
      for (const option of this.options) {
        const control = this.form.controls[this.controlFor(option.name)];
        if (option.alreadyTracked) control.disable();
        else control.enable();
      }
    });
  }

  get options() {
    return this.optionsState();
  }

  controlFor(name: string): 'eventGoldStop' | 'eventKecleon' | 'eventShowcase' {
    return name === 'kecleon' ? 'eventKecleon' : name === 'gold-stop' ? 'eventGoldStop' : 'eventShowcase';
  }

  /** The refusal named a stored rule: close and let the list open that one for editing. */
  editExisting(rule: ExistingRule): void {
    this.dialogRef.close(editExistingResult(rule));
  }

  hasSelection(): boolean {
    return this.chosen().length > 0;
  }

  async save(): Promise<void> {
    const chosen = this.chosen();
    if (chosen.length === 0) return;

    this.saving.set(true);
    this.refusal.set(null);
    const v = this.form.getRawValue();
    const scope = scopeToFields(this.scope());
    const single = chosen.length === 1;

    // One request per event. The API refuses a second alarm for an event the profile already tracks,
    // so a partial success is possible and each is reported on its own rather than as one verdict.
    let created = 0;
    let unchanged = 0;
    for (const displayType of chosen) {
      try {
        const result = await firstValueFrom(
          this.pokestopEventService.create({
            overrideAreas: scope.overrideAreas,
            overrideLocationLabel: scope.overrideLocationLabel,
            clean: v.autoDelete ? AUTO_DELETE : 0,
            displayType,
            distance: scope.distance,
            template: v.template || null,
          }),
        );
        if ((result?.uid ?? 0) > 0) created++;
        else unchanged++;
      } catch (err) {
        this.saving.set(false);
        if (single) {
          // Nothing was created, so keep the dialog and the user's choices, and say why inline.
          this.refusal.set(refusalOf(err, this.i18n.instant('ALARM.FAILED_CREATE'), (uid, type) => this.describe(uid, type)));
          return;
        }
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('ALARM.FAILED_CREATE'), this.i18n.instant('COMMON.OK'), {
          duration: 6000,
        });
        // Whatever was written before the failure still exists, so close and reload rather than
        // leaving the list showing a state that is already out of date.
        this.dialogRef.close(created + unchanged > 0);
        return;
      }
    }

    // A single event PoracleNG found already stored exactly was left as it is; saying "created" hid that.
    reportCreated(created, unchanged, this.i18n, this.snackBar);
    this.dialogRef.close(true);
  }

  private chosen(): number[] {
    const v = this.form.getRawValue();
    return this.options.filter(o => !o.alreadyTracked && v[this.controlFor(o.name)]).map(o => o.displayType);
  }

  /** Names a stored event rule the API reported in the way, from the rules the list passed in. */
  private describe(uid: number, trackingType: null | string): null | string {
    if (trackingType !== null && trackingType !== 'incident') return null;
    const rule = this.existing.find(x => x.uid === uid);
    if (!rule) return null;
    const event = POKESTOP_EVENTS.find(e => e.displayType === rule.displayType);
    return ruleSummary(event ? this.translate.instant(event.displayKey) : '', rule, this.data?.profileAreas ?? [], this.translate);
  }
}

/**
 * Whether a stored rule has the scope a save would send. "No override" has several spellings (null,
 * [] and "" all mean none); area names compare lowercased but in order, as PoracleNG keeps them.
 */
function sameScope(rule: PokestopEvent, scope: { distance: number; overrideAreas: string[]; overrideLocationLabel: string }): boolean {
  const areas = (rule.overrideAreas ?? []).map(a => a.toLowerCase());
  const wanted = scope.overrideAreas.map(a => a.toLowerCase());
  return (
    (rule.distance ?? 0) === scope.distance &&
    (rule.overrideLocationLabel ?? '') === scope.overrideLocationLabel &&
    areas.length === wanted.length &&
    areas.every((a, i) => a === wanted[i])
  );
}
