import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

import { DriftItem, LinkSide, ReverseGeocodingResult } from '../../../core/models';
import { AreaNameService } from '../../../core/services/area-name.service';
import { AreaService } from '../../../core/services/area.service';
import { I18nService } from '../../../core/services/i18n.service';
import { LinkedAccountService } from '../../../core/services/linked-account.service';
import { LocationService } from '../../../core/services/location.service';
import { SettingsService } from '../../../core/services/settings.service';
import {
  DRIFT_SIDES,
  DriftLine,
  DriftPoint,
  RESOLVED_KEYS,
  areaKey,
  describeDrift,
  isDriftPoint,
  otherSide,
  pointKey,
} from '../../utils/pair-drift';
import { serverReason } from '../../utils/server-reason';
import { ConfirmDialogComponent, ConfirmDialogData } from '../confirm-dialog/confirm-dialog.component';

/** The refusal codes `POST /api/account-link/resolve` answers 409 with. */
const ERROR_KEYS: Record<string, string> = {
  drift_changed: 'DRIFT.ERR_CHANGED',
  partially_applied: 'DRIFT.ERR_PARTIAL',
  place_in_use: 'DRIFT.ERR_PLACE_IN_USE',
  profile_holds_alarms: 'DRIFT.ERR_PROFILE_HOLDS_ALARMS',
  profile_missing: 'DRIFT.ERR_PROFILE_MISSING',
};

/**
 * Every setting a linked pair's apps disagree on, with what each choice would do to the other app. It is the
 * one place to settle them: the banner only points here, and dismissing the banner never hides anything here.
 *
 * Nothing is resolved without the person choosing, and a choice that deletes a profile or saved place on the
 * other app asks first, naming them. The server answers a choice only against the fingerprint of what was shown.
 * After a choice the outcome is announced, the list is read again, and focus moves to the next difference (or
 * the heading when none are left), since the button that was pressed may be gone.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, TranslatePipe],
  selector: 'app-pair-drift-panel',
  styleUrl: './pair-drift-panel.component.scss',
  templateUrl: './pair-drift-panel.component.html',
})
export class PairDriftPanelComponent {
  /** Admin areas as PoracleNG names them, by comparison key, so "ACLT" is never shown as "Aclt". */
  private readonly areaDisplay = signal<Map<string, string>>(new Map());
  private readonly areaNames = inject(AreaNameService);
  /** Asked for once, and only when an areas line needs Poracle's spelling. */
  private areasRequested = false;
  private readonly dialog = inject(MatDialog);
  private readonly geo = signal<Map<string, ReverseGeocodingResult>>(new Map());
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly requested = new Set<string>();
  private readonly translate = inject(TranslateService);

  protected readonly busy = signal<null | string>(null);
  protected readonly error = signal('');
  protected readonly linked = inject(LinkedAccountService);
  protected readonly lines = computed<DriftLine[]>(() => {
    const lang = this.i18n.currentLang(); // composed sentences follow the display language
    const geo = this.geo();
    const display = this.areaDisplay();
    return this.linked.drift().map(item =>
      describeDrift(item, {
        side: side => this.sideName(side),
        area: name => display.get(areaKey(name)) ?? this.areaNames.label(name),
        geo: (p: DriftPoint) => geo.get(pointKey(p)),
        lang,
        t: (key, params) => this.translate.instant(key, params),
      }),
    );
  });

  protected readonly message = signal('');
  protected readonly sides = DRIFT_SIDES;

  constructor() {
    // Services that read from the API are reached only once there is something to look up.
    effect(() => {
      if (!this.linked.canChoose()) return;
      const items = this.linked.drift();
      untracked(() => {
        this.lookUpPlaces(items);
        if (items.some(i => i.field === 'areas')) this.lookUpAreaNames();
      });
    });
  }

  protected consequenceId(line: DriftLine, side: LinkSide): string {
    return `drift-consequence-${line.item.field}-${side}`;
  }

  protected async keep(line: DriftLine, side: LinkSide): Promise<void> {
    if (this.busy() || line.blocked[side]) return;
    const removes = line.removes[side];
    if (removes.length > 0 && !(await this.confirmRemoval(side, removes))) return;

    const index = this.lines().findIndex(l => l.item.field === line.item.field);
    this.busy.set(line.item.field + side);
    this.message.set('');
    this.error.set('');
    let succeeded = false;
    try {
      await this.linked.resolve(line.item.field, side, line.item.fingerprint);
      succeeded = true;
      this.message.set(
        this.translate.instant(RESOLVED_KEYS[line.item.field], { kept: this.sideName(side), other: this.sideName(otherSide(side)) }),
      );
    } catch (err) {
      // The service has already read the status again, so a stale line has gone or changed by now.
      this.error.set(this.errorText(err));
    } finally {
      this.busy.set(null);
    }
    this.focusAfter(succeeded ? index : this.lines().findIndex(l => l.item.field === line.item.field), succeeded ? null : side);
  }

  protected sideName(side: LinkSide): string {
    return this.translate.instant(side === 'discord' ? 'LINKED.SIDE_DISCORD' : 'LINKED.SIDE_TELEGRAM');
  }

  private async confirmRemoval(side: LinkSide, names: string[]): Promise<boolean> {
    const other = this.sideName(otherSide(side));
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.translate.instant('DRIFT.KEEP', { side: this.sideName(side) }),
        items: names,
        message: this.translate.instant('DRIFT.CONFIRM_MESSAGE', { kept: this.sideName(side), other }),
        title: this.translate.instant('DRIFT.CONFIRM_TITLE', { other }),
        warn: true,
      } as ConfirmDialogData,
    });
    return !!(await firstValueFrom(ref.afterClosed(), { defaultValue: false }));
  }

  private errorText(err: unknown): string {
    const body = (err as { error?: { code?: unknown; count?: unknown; name?: unknown; side?: unknown } } | null)?.error;
    const key = typeof body?.code === 'string' ? ERROR_KEYS[body.code] : undefined;
    if (key) {
      const side = body?.side === 'discord' || body?.side === 'telegram' ? this.sideName(body.side) : '';
      return this.translate.instant(key, { name: body?.name ?? '', side, count: body?.count ?? '' });
    }
    return serverReason(err) ?? this.translate.instant('DRIFT.RESOLVE_FAILED');
  }

  /**
   * After a success, the difference now at the pressed one's place (the next one), else the heading. After a failure,
   * the button pressed if its line is still there, else the heading.
   */
  private focusAfter(index: number, side: LinkSide | null): void {
    afterNextRender(
      () => {
        const root = this.host.nativeElement;
        const lines = this.lines();
        let target: HTMLElement | null = null;
        if (index >= 0 && lines.length > 0) {
          const line = lines[Math.min(index, lines.length - 1)];
          target =
            side === null
              ? root.querySelector<HTMLElement>(`#drift-label-${line.item.field}`)
              : root.querySelector<HTMLElement>(`[data-keep="${line.item.field}-${side}"]`);
        }
        (target ?? root.querySelector<HTMLElement>('#drift-panel-heading'))?.focus();
      },
      { injector: this.injector },
    );
  }

  /** Admin areas as PoracleNG spells them. Skipped under `disable_areas`, whose routes would refuse. */
  private lookUpAreaNames(): void {
    if (this.areasRequested || this.injector.get(SettingsService).isDisabled('disable_areas')) return;
    this.areasRequested = true;
    this.injector
      .get(AreaService)
      .getAvailable()
      .subscribe({
        error: () => undefined, // the stored names are shown instead
        next: defs => this.areaDisplay.set(new Map(defs.filter(d => d?.name).map(d => [areaKey(d.name), d.name]))),
      });
  }

  /** A place name for each side of a location difference, through the same lookup the location dialog uses. */
  private lookUpPlaces(items: DriftItem[]): void {
    if (!items.some(i => i.field === 'location')) return;
    const locations = this.injector.get(LocationService);
    if (locations.geocodingDisabled()) return;
    for (const item of items) {
      if (item.field !== 'location') continue;
      for (const point of [item.discord, item.telegram]) {
        if (!isDriftPoint(point) || (point.lat === 0 && point.lon === 0)) continue;
        const key = pointKey(point);
        if (this.requested.has(key)) continue;
        this.requested.add(key);
        locations.reverseGeocode(point.lat, point.lon).subscribe(result => {
          if (result) this.geo.update(m => new Map(m).set(key, result));
        });
      }
    }
  }
}
