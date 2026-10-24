import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

import { DriftItem, LinkSide } from '../../../core/models';
import { AreaNameService } from '../../../core/services/area-name.service';
import { LinkedAccountService } from '../../../core/services/linked-account.service';
import { LocationService } from '../../../core/services/location.service';
import { ToastService } from '../../../core/services/toast.service';
import { serverReason } from '../../utils/server-reason';

const DISMISSED_KEY = 'poracle-drift-dismissed';

interface Point {
  lat: number;
  lon: number;
}

interface DriftLine {
  item: DriftItem;
  text: string;
}

function pointKey(p: Point): string {
  return `${p.lat},${p.lon}`;
}

function isPoint(value: unknown): value is Point {
  const p = value as null | Point;
  return typeof p?.lat === 'number' && typeof p?.lon === 'number';
}

function readDismissed(): Set<string> {
  try {
    const parsed: unknown = JSON.parse(sessionStorage.getItem(DISMISSED_KEY) ?? '[]');
    return new Set(Array.isArray(parsed) ? parsed.filter((v): v is string => typeof v === 'string') : []);
  } catch {
    return new Set();
  }
}

/**
 * One line for each setting a linked pair disagrees on, and a button per side to keep. Nothing is ever
 * resolved without the person choosing: the server answers a choice only against the fingerprint of what
 * was shown here. Dismissing hides what has been seen for the rest of the session; a difference that
 * appears later is a new fingerprint and shows again.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  selector: 'app-pair-drift-banner',
  styleUrl: './pair-drift-banner.component.scss',
  templateUrl: './pair-drift-banner.component.html',
})
export class PairDriftBannerComponent {
  private readonly areaNames = inject(AreaNameService);
  private readonly dismissed = signal<Set<string>>(readDismissed());
  private readonly linked = inject(LinkedAccountService);
  private readonly locations = inject(LocationService);
  private readonly names = signal<Map<string, string>>(new Map());
  private readonly requested = new Set<string>();
  private readonly toast = inject(ToastService);
  private readonly translate = inject(TranslateService);

  /** The item and side being resolved, so its buttons can't be pressed twice. */
  protected readonly busy = signal<null | string>(null);

  protected readonly lines = computed<DriftLine[]>(() => {
    this.translate.currentLang(); // composed sentences follow the display language
    const names = this.names();
    return this.linked.drift().map(item => ({ item, text: this.describe(item, names) }));
  });

  protected readonly sides: LinkSide[] = ['discord', 'telegram'];

  protected readonly visible = computed(() => this.linked.drift().some(i => !this.dismissed().has(i.fingerprint)));

  constructor() {
    effect(() => {
      const items = this.linked.drift();
      untracked(() => this.lookUpPlaces(items));
    });
  }

  protected dismiss(): void {
    const next = new Set(this.dismissed());
    for (const item of this.linked.drift()) next.add(item.fingerprint);
    this.dismissed.set(next);
    try {
      sessionStorage.setItem(DISMISSED_KEY, JSON.stringify([...next]));
    } catch {
      // The banner stays hidden for this page load without it.
    }
  }

  protected async keep(item: DriftItem, side: LinkSide): Promise<void> {
    if (this.busy()) return;
    this.busy.set(item.fingerprint + side);
    try {
      await this.linked.resolve(item.field, side, item.fingerprint);
    } catch (err) {
      // The service has already read the status again, so a stale line goes away by itself.
      this.toast.error(serverReason(err) ?? this.translate.instant('DRIFT.RESOLVE_FAILED'));
    } finally {
      this.busy.set(null);
    }
  }

  protected keepAria(line: DriftLine, side: LinkSide): string {
    return this.translate.instant('DRIFT.KEEP_ARIA', { side: this.sideName(side), line: line.text });
  }

  protected sideName(side: LinkSide): string {
    return this.translate.instant(side === 'discord' ? 'LINKED.SIDE_DISCORD' : 'LINKED.SIDE_TELEGRAM');
  }

  private describe(item: DriftItem, names: Map<string, string>): string {
    const t = (key: string, params?: Record<string, unknown>) => this.translate.instant(key, params);
    const otherOf = (side: LinkSide): LinkSide => (side === 'discord' ? 'telegram' : 'discord');
    const sentences = (extra: (side: LinkSide) => string[], key: string): string[] =>
      this.sides.filter(s => extra(s).length > 0).map(s => t(key, { names: extra(s).join(', '), side: this.sideName(s) }));
    const list = <T>(side: LinkSide): T[] => (Array.isArray(item[side]) ? (item[side] as T[]) : []);

    switch (item.field) {
      case 'areas':
        return sentences(
          side =>
            list<string>(side)
              .filter(a => !list<string>(otherOf(side)).includes(a))
              .map(a => this.areaNames.label(a)),
          'DRIFT.AREAS_EXTRA',
        ).join(' ');
      case 'current_profile':
        return t('DRIFT.PROFILE_CURRENT', { discord: String(item.discord), telegram: String(item.telegram) });
      case 'location': {
        const place = (v: unknown) => (isPoint(v) ? (names.get(pointKey(v)) ?? `${v.lat}, ${v.lon}`) : '');
        return t('DRIFT.LOCATION', { discord: place(item.discord), telegram: place(item.telegram) });
      }
      case 'places': {
        const found = sentences(
          side =>
            list<{ label: string }>(side)
              .filter(r => !list<{ label: string }>(otherOf(side)).some(o => o.label === r.label))
              .map(r => this.areaNames.label(r.label)),
          'DRIFT.PLACES_EXTRA',
        );
        return found.length ? found.join(' ') : t('DRIFT.PLACES_DIFFER');
      }
      case 'profiles': {
        const found = sentences(
          side =>
            list<{ name?: string; no: number }>(side)
              .filter(r => !list<{ no: number }>(otherOf(side)).some(o => o.no === r.no))
              .map(r => r.name || t('DRIFT.PROFILE_NAMELESS', { no: r.no })),
          'DRIFT.PROFILES_EXTRA',
        );
        return found.length ? found.join(' ') : t('DRIFT.PROFILES_DIFFER');
      }
      default:
        return '';
    }
  }

  /** A place name for each side of a location difference, through the same lookup the location dialog uses. */
  private lookUpPlaces(items: DriftItem[]): void {
    if (this.locations.geocodingDisabled()) return;
    for (const item of items) {
      if (item.field !== 'location') continue;
      for (const point of [item.discord, item.telegram]) {
        if (!isPoint(point)) continue;
        const key = pointKey(point);
        if (this.requested.has(key)) continue;
        this.requested.add(key);
        this.locations.reverseGeocode(point.lat, point.lon).subscribe(result => {
          const name = result?.city || result?.neighbourhood || result?.displayName;
          if (!name) return;
          this.names.update(m => new Map(m).set(key, name));
        });
      }
    }
  }
}
