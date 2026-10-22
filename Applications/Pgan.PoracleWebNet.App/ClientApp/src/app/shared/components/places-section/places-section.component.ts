import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

import { translatePlural } from '../../../core/i18n/plural';
import { Location, SavedPlace } from '../../../core/models';
import { PlacesService } from '../../../core/services/places.service';
import { describeReferencingRule, PLACE_NAME_PROMPT } from '../../utils/place-label';
import { serverReason } from '../../utils/server-reason';
import { ConfirmDialogComponent } from '../confirm-dialog/confirm-dialog.component';
import { LocationDialogComponent } from '../location-dialog/location-dialog.component';

/**
 * The places a user's alarms can be aimed at: the profile pin, plus whatever they have named.
 *
 * A section of the Areas page, directly under the Location card that holds the pin. The pin and the
 * named places are the same kind of thing — points an alert can measure from — and three design
 * reviews all landed on the same objection to giving them separate homes. The pin is not repeated in
 * this grid because the card immediately above it is the pin.
 *
 * Adding a place borrows the location dialog as a coordinate picker rather than growing a second map,
 * then asks for the name separately, because picking a point and naming it are two decisions and
 * putting them on one screen makes both feel like a form.
 *
 * Moving one reuses the same picker and skips the naming step, because the label is what every alarm
 * points at and changing it is a different act. Every supported PoracleNG (5.4.0 and up) can do it, so
 * the control is always shown and nothing asks the server first.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  selector: 'app-places-section',
  standalone: true,
  styleUrl: './places-section.component.scss',
  templateUrl: './places-section.component.html',
})
export class PlacesSectionComponent implements OnInit {
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  readonly busy = signal(false);

  readonly loading = signal(true);
  readonly places = inject(PlacesService);
  /** Placeholder count while loading: enough to fill a row without implying how many you have. */
  readonly skeletons = [0, 1, 2];

  addPlace(): void {
    const picker = this.dialog.open(LocationDialogComponent, {
      data: { latitude: this.places.pin()?.latitude ?? 0, longitude: this.places.pin()?.longitude ?? 0, pickOnly: true },
    });

    picker.afterClosed().subscribe((point?: Location) => {
      if (!point) return;
      this.nameAndSave(point);
    });
  }

  confirmRemove(place: SavedPlace): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          confirmText: this.translate.instant('COMMON.DELETE'),
          message: this.translate.instant('WHERE.PLACE_DELETE_CONFIRM', { place: place.label }),
          title: this.translate.instant('WHERE.PLACE_DELETE_TITLE'),
        },
      })
      .afterClosed()
      .subscribe(confirmed => {
        if (confirmed) this.removePlace(place);
      });
  }

  /** Moves a place to a new point, keeping its label so every alarm pointing at it follows. */
  movePlace(place: SavedPlace): void {
    const picker = this.dialog.open(LocationDialogComponent, {
      data: {
        latitude: place.latitude,
        longitude: place.longitude,
        pickOnly: true,
        title: this.translate.instant('WHERE.PLACE_MOVE_NAMED', { place: place.label }),
      },
    });

    picker.afterClosed().subscribe((point?: Location) => {
      if (!point) return;

      this.busy.set(true);
      this.places.move(place.label, point.latitude, point.longitude).subscribe({
        error: () => {
          this.busy.set(false);
          this.snackBar.open(this.translate.instant('WHERE.PLACE_MOVE_ERROR'), this.translate.instant('COMMON.OK'), {
            duration: 6000,
          });
        },
        next: () => {
          this.busy.set(false);
          this.snackBar.open(this.translate.instant('WHERE.PLACE_MOVED', { place: place.label }), this.translate.instant('COMMON.OK'), {
            duration: 3000,
          });
        },
      });
    });
  }

  ngOnInit(): void {
    this.reload();
  }

  removePlace(place: SavedPlace): void {
    this.busy.set(true);
    this.places.remove(place.label).subscribe({
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);

        // 409 carries the alarms still pointing at the place. Naming them, not just counting them, is the
        // difference between "could not delete" and knowing what to repoint first.
        const rules: string[] = err.status === 409 ? (err.error?.referencingRules ?? []) : [];
        this.snackBar.open(
          rules.length > 0
            ? translatePlural(this.translate, this.translate.getCurrentLang() ?? 'en', 'WHERE.PLACE_IN_USE', rules.length, {
                place: place.label,
                rules: this.listRules(rules),
              })
            : (serverReason(err) ?? this.translate.instant('WHERE.PLACE_DELETE_ERROR')),
          this.translate.instant('COMMON.OK'),
          { duration: rules.length > 0 ? 12000 : 6000 },
        );
      },
      next: () => {
        this.busy.set(false);
        this.snackBar.open(this.translate.instant('WHERE.PLACE_DELETED', { place: place.label }), this.translate.instant('COMMON.OK'), {
          duration: 3000,
        });
      },
    });
  }

  /** The first few alarms in the way, then "and N more", so a long list still fits a snackbar. */
  private listRules(rules: string[]): string {
    const shown = rules.slice(0, 5).map(rule => describeReferencingRule(rule, key => this.translate.instant(key)));
    return rules.length > shown.length
      ? this.translate.instant('WHERE.AREA_LIST_MORE', { areas: shown.join(', '), count: rules.length - shown.length })
      : shown.join(', ');
  }

  private nameAndSave(point: Location): void {
    // ConfirmDialog's promptField already does the name-with-duplicate-check, so this reuses it rather
    // than adding a third dialog that asks for a single string.
    const naming = this.dialog.open(ConfirmDialogComponent, {
      width: '420px',
      data: {
        confirmText: this.translate.instant('COMMON.SAVE'),
        message: this.translate.instant('WHERE.NAME_PLACE_MESSAGE'),
        promptField: {
          ...PLACE_NAME_PROMPT,
          existingNames: this.places.named().map(p => p.label),
          label: this.translate.instant('WHERE.PLACE_NAME'),
          value: '',
        },
        title: this.translate.instant('WHERE.NAME_PLACE_TITLE'),
      },
    });

    naming.afterClosed().subscribe((label?: false | string) => {
      if (!label) return;

      this.busy.set(true);
      this.places.add({ label, latitude: point.latitude, longitude: point.longitude }).subscribe({
        error: (err: HttpErrorResponse) => {
          this.busy.set(false);
          // The API answers a refused label (its own rule, or PoracleNG's duplicate) with a 400 carrying the
          // reason. Showing that beats a generic failure.
          this.snackBar.open(serverReason(err) ?? this.translate.instant('WHERE.PLACE_SAVE_ERROR'), this.translate.instant('COMMON.OK'), {
            duration: 6000,
          });
        },
        next: () => {
          this.busy.set(false);
          this.snackBar.open(this.translate.instant('WHERE.PLACE_SAVED', { place: label }), this.translate.instant('COMMON.OK'), {
            duration: 3000,
          });
        },
      });
    });
  }

  private reload(): void {
    this.loading.set(true);
    this.places.load().subscribe({
      error: () => this.loading.set(false),
      next: () => this.loading.set(false),
    });
  }
}
