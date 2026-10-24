import { HttpEventType, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { tap } from 'rxjs';

import { I18nService } from '../services/i18n.service';
import { ToastService } from '../services/toast.service';

const HEADER = 'X-Pair-Warning';

/** Longer than an error toast (5 s): the message carries a reason sentence the person has to read. */
export const PAIR_WARNING_TOAST_MS = 8000;
const SIDES = ['discord', 'telegram'] as const;

/**
 * Says so when a write reached only one side of a linked pair. The API reports it as an `X-Pair-Warning`
 * header (`{"side":"telegram","reason":"..."}`, the side that did not take it) and never in a body, so
 * this reads the header and nothing else: no endpoint's body parsing changes, and every person-level
 * write is covered from one place. One response is one message.
 */
export const pairWarningInterceptor: HttpInterceptorFn = (req, next) => {
  const i18n = inject(I18nService);
  const toast = inject(ToastService);
  return next(req).pipe(
    tap(event => {
      if (event.type !== HttpEventType.Response) return;
      const raw = event.headers.get(HEADER);
      if (!raw) return;
      let parsed: { reason?: unknown; side?: unknown };
      try {
        parsed = JSON.parse(raw);
      } catch {
        return;
      }
      const failed = SIDES.find(s => s === parsed?.side);
      if (!failed) return;
      const saved = failed === 'discord' ? 'telegram' : 'discord';
      const name = (side: string) => i18n.instant(side === 'discord' ? 'LINKED.SIDE_DISCORD' : 'LINKED.SIDE_TELEGRAM');
      toast.info(
        i18n.instant('DRIFT.PAIR_WARNING', {
          failed: name(failed),
          reason: typeof parsed.reason === 'string' ? parsed.reason : '',
          saved: name(saved),
        }),
        PAIR_WARNING_TOAST_MS,
      );
    }),
  );
};
