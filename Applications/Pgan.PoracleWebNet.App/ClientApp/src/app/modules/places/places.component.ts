import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

import { Location, SavedPlace } from '../../core/models';
import { PlacesService } from '../../core/services/places.service';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { LocationDialogComponent } from '../../shared/components/location-dialog/location-dialog.component';

/**
 * The places a user's alarms can be aimed at: the profile pin, plus whatever they have named.
 *
 * A page rather than a dialog, and in the nav beside Areas and My Geofences, because these are the
 * user's data and not a preference. The Areas page already frames alerts as reaching you by area or by
 * a radius from a point; named points are more of the second, so hiding them in a settings menu split
 * one idea across two surfaces.
 *
 * Adding a place borrows the location dialog as a coordinate picker rather than growing a second map,
 * then asks for the name separately, because picking a point and naming it are two decisions and
 * putting them on one screen makes both feel like a form.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  selector: 'app-places',
  standalone: true,
  styleUrl: './places.component.scss',
  templateUrl: './places.component.html',
})
export class PlacesComponent implements OnInit {
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
      width: '600px',
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

  ngOnInit(): void {
    this.reload();
  }

  removePlace(place: SavedPlace): void {
    this.busy.set(true);
    this.places.remove(place.label).subscribe({
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);

        // 409 carries the alarms still pointing at the place. Naming them is the difference between
        // "could not delete" and knowing what to repoint first.
        const rules: string[] = err.status === 409 ? (err.error?.referencingRules ?? []) : [];
        this.snackBar.open(
          rules.length > 0
            ? this.translate.instant('WHERE.PLACE_IN_USE', { count: rules.length, place: place.label })
            : this.translate.instant('WHERE.PLACE_DELETE_ERROR'),
          this.translate.instant('COMMON.OK'),
          { duration: 6000 },
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

  private nameAndSave(point: Location): void {
    // ConfirmDialog's promptField already does the name-with-duplicate-check, so this reuses it rather
    // than adding a third dialog that asks for a single string.
    const naming = this.dialog.open(ConfirmDialogComponent, {
      width: '420px',
      data: {
        confirmText: this.translate.instant('COMMON.SAVE'),
        message: this.translate.instant('WHERE.NAME_PLACE_MESSAGE'),
        promptField: {
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
          // PoracleNG reports a rejected label inside its own response, so the API turns it into a 400
          // with the reason. Showing that beats a generic failure: it is usually "you already have one".
          this.snackBar.open(err.error?.error ?? this.translate.instant('WHERE.PLACE_SAVE_ERROR'), this.translate.instant('COMMON.OK'), {
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
