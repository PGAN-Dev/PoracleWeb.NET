import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

import { AdminArea, AdminAreaService } from '../../core/services/admin-area.service';
import { I18nService } from '../../core/services/i18n.service';

/**
 * An area plus what the server last told us about it, so the page can tell a staged change from a
 * saved one without re-fetching.
 */
interface AreaRow extends AdminArea {
  storedHidden: boolean;
}

/**
 * Takes an area off the menu: staging fences, test polygons, regions the instance covers but does not
 * advertise. See #885.
 *
 * Hiding writes `userSelectable: false` into the geofence feed this site serves, which reaches the
 * Areas page, the per-alarm scope picker and the bot's area picker at once. It does NOT unsubscribe
 * anyone already using the area, and the page says so rather than letting an operator assume it did.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    MatTooltipModule,
    TranslatePipe,
  ],
  selector: 'app-admin-areas',
  standalone: true,
  styleUrl: './admin-areas.component.scss',
  templateUrl: './admin-areas.component.html',
})
export class AdminAreasComponent implements OnInit {
  readonly areas = signal<AreaRow[]>([]);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly orphaned = signal<string[]>([]);
  readonly saving = signal(false);
  readonly search = signal('');

  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly service = inject(AdminAreaService);
  private readonly snackBar = inject(MatSnackBar);

  /** The hidden set as staged in the UI, which is what a save sends. */
  private readonly hidden = signal<Set<string>>(new Set());

  readonly hiddenCount = computed(() => this.hidden().size);

  readonly dirty = computed(() => {
    const staged = this.hidden();
    const stored = this.areas()
      .filter(a => a.storedHidden)
      .map(a => a.name);
    return stored.length !== staged.size || stored.some(n => !staged.has(n));
  });

  readonly visible = computed(() => {
    const q = this.search().trim().toLowerCase();
    const rows = this.areas();
    if (!q) return rows;
    return rows.filter(a => a.name.toLowerCase().includes(q) || (a.group ?? '').toLowerCase().includes(q));
  });

  ngOnInit(): void {
    this.reload();
  }

  isHidden(area: AreaRow): boolean {
    return this.hidden().has(area.name);
  }

  toggle(area: AreaRow, hide: boolean): void {
    this.hidden.update(current => {
      const next = new Set(current);
      if (hide) next.add(area.name);
      else next.delete(area.name);
      return next;
    });
  }

  discard(): void {
    this.hidden.set(
      new Set(
        this.areas()
          .filter(a => a.storedHidden)
          .map(a => a.name),
      ),
    );
  }

  save(): void {
    this.saving.set(true);
    // Orphaned names are sent back unchanged. They are hidden areas Koji is not serving right now,
    // and dropping them because they are absent would turn a Koji outage into a silent un-hiding.
    const names = [...this.hidden(), ...this.orphaned()];
    this.service
      .setHidden(names)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.saving.set(false);
          this.snackBar.open(this.i18n.instant('ADMIN_AREAS.SAVE_FAILED'), this.i18n.instant('COMMON.OK'), { duration: 5000 });
        },
        next: result => {
          this.saving.set(false);
          this.areas.update(rows => rows.map(a => ({ ...a, storedHidden: this.hidden().has(a.name) })));
          // "Saved but not live" is a different state from "saved", and an operator checking the bot
          // needs to know which one they are in.
          const key = result.reloaded ? 'ADMIN_AREAS.SAVED' : 'ADMIN_AREAS.SAVED_NOT_RELOADED';
          this.snackBar.open(this.i18n.instant(key), this.i18n.instant('COMMON.OK'), { duration: result.reloaded ? 3000 : 8000 });
        },
      });
  }

  private reload(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .getAreas()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
          this.loadFailed.set(true);
        },
        next: list => {
          this.areas.set((list.areas ?? []).map(a => ({ ...a, storedHidden: a.hidden })));
          this.orphaned.set(list.orphaned ?? []);
          this.hidden.set(new Set((list.areas ?? []).filter(a => a.hidden).map(a => a.name)));
          this.loading.set(false);
        },
      });
  }
}
