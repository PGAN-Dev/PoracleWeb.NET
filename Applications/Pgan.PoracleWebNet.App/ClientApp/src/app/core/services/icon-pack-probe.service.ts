import { Injectable } from '@angular/core';

import { IconSourceKey } from './icon.service';
import { ICON_REPO_PROBES, normalizeRepoBase } from '../../shared/utils/icon-repos';

export interface IconPackProbeResult {
  /** The base that was probed, normalized. */
  base: string;
  /** Categories whose representative file did not load. Empty means the pack is usable. */
  missing: IconSourceKey[];
}

/** Long enough for a cold CDN, short enough that a dead host does not hold the dialog open. */
const PROBE_TIMEOUT_MS = 10_000;

/**
 * Asks a candidate pack whether it can actually dress this site, by loading one representative file
 * per category.
 *
 * Deliberately images rather than the pack's `index.json`. Every UICONS pack publishes one and it
 * would give a more precise answer, but reading it is a cross-origin `fetch` and needs CORS headers.
 * GitHub sends them; a pack served off an operator's own nginx very likely does not, and refusing
 * such a pack would be refusing something that works -- `<img>` has never needed CORS. So the probe
 * asks the question the browser will actually ask when it renders the page.
 *
 * The cost is that it cannot tell "no `type/` folder" from "`type/12.png` is missing". Both mean the
 * type icons will not render, which is what the operator needs to know.
 */
@Injectable({ providedIn: 'root' })
export class IconPackProbeService {
  /** Overridable so tests do not need a network or a real DOM image. */
  protected createImage(): HTMLImageElement {
    return new Image();
  }

  async probe(base: string): Promise<IconPackProbeResult> {
    const normalized = normalizeRepoBase(base);
    const results = await Promise.all(
      ICON_REPO_PROBES.map(async ({ key, path }) => ({ key, ok: await this.loads(`${normalized}/${path}`) })),
    );

    return { base: normalized, missing: results.filter(r => !r.ok).map(r => r.key) };
  }

  private loads(url: string): Promise<boolean> {
    return new Promise<boolean>(resolve => {
      const img = this.createImage();
      let settled = false;
      const finish = (ok: boolean) => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        img.onload = null;
        img.onerror = null;
        resolve(ok);
      };
      const timer = setTimeout(() => {
        // Point the element at nothing so a slow response cannot resolve after the caller moved on.
        img.src = '';
        finish(false);
      }, PROBE_TIMEOUT_MS);

      img.onload = () => finish(img.naturalWidth > 0);
      img.onerror = () => finish(false);
      img.src = url;
    });
  }
}
