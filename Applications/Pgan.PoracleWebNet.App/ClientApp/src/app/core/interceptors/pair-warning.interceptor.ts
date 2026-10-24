import { HttpEventType, HttpInterceptorFn } from '@angular/common/http';
import { Injector, inject } from '@angular/core';
import { tap } from 'rxjs';

import { I18nService } from '../services/i18n.service';
import { LinkedAccountService } from '../services/linked-account.service';
import { PAIR_WARNING_TOAST_MS, ToastService } from '../services/toast.service';

export { PAIR_WARNING_TOAST_MS };

const HEADER = 'X-Pair-Warning';
const SIDES = ['discord', 'telegram'] as const;

/** The reason codes the API sends, as the words that finish "it couldn't be saved for Telegram: …". */
const REASON_KEYS: Record<string, string> = {
  rename_refused: 'DRIFT.WARN_RENAME_REFUSED',
  out_of_step: 'DRIFT.WARN_OUT_OF_STEP',
  unreachable: 'DRIFT.WARN_UNREACHABLE',
};

/** Writes that can't change what the two apps hold, or that read the drift again themselves. */
const NO_DRIFT_REFRESH = /\/api\/(account-link|test-alert|auth)(\/|$)/;

/**
 * Says so when a write reached only one side of a linked pair. The API reports it as an `X-Pair-Warning`
 * header (`{"side":"telegram","reason":"...","code":"unreachable"}`, the side that did not take it) and never in
 * a body, so this reads the header and nothing else: no endpoint's body parsing changes, and every person-level
 * write is covered from one place. One response is one message, and the page's own success toast carries it
 * rather than replacing it (`ToastService.pairWarning`).
 *
 * Any write may also change what the two apps disagree on, so after one the drift is read again: a stale
 * banner or panel would offer to undo what the person just did.
 */
export const pairWarningInterceptor: HttpInterceptorFn = (req, next) => {
  const i18n = inject(I18nService);
  const toast = inject(ToastService);
  const injector = inject(Injector);
  const isWrite = req.method !== 'GET' && req.method !== 'HEAD' && req.method !== 'OPTIONS' && !NO_DRIFT_REFRESH.test(req.url);
  return next(req).pipe(
    tap(event => {
      if (event.type !== HttpEventType.Response) return;
      // Read lazily: the service itself uses HttpClient, which is what this interceptor is part of.
      if (isWrite) injector.get(LinkedAccountService).refreshSoon();
      const raw = event.headers.get(HEADER);
      if (!raw) return;
      let parsed: { code?: unknown; reason?: unknown; side?: unknown };
      try {
        parsed = JSON.parse(raw);
      } catch {
        return;
      }
      const failed = SIDES.find(s => s === parsed?.side);
      if (!failed) return;
      const saved = failed === 'discord' ? 'telegram' : 'discord';
      const name = (side: string) => i18n.instant(side === 'discord' ? 'LINKED.SIDE_DISCORD' : 'LINKED.SIDE_TELEGRAM');
      toast.pairWarning(
        i18n.instant('DRIFT.PAIR_WARNING', {
          failed: name(failed),
          reason: reasonText(parsed, name(saved), i18n),
          saved: name(saved),
        }),
      );
    }),
  );
};

/**
 * A known code in the display language. A refusal carries PoracleNG's own sentence, which is shown as it came,
 * without its full stop so the template's punctuation is the only one.
 */
function reasonText(parsed: { code?: unknown; reason?: unknown }, saved: string, i18n: I18nService): string {
  const key = typeof parsed.code === 'string' ? REASON_KEYS[parsed.code] : undefined;
  if (key) return i18n.instant(key, { saved });
  const raw = typeof parsed.reason === 'string' ? parsed.reason.trim().replace(/[.\s]+$/, '') : '';
  return raw || i18n.instant('DRIFT.WARN_REFUSED');
}
