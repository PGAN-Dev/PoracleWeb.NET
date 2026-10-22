import { signal } from '@angular/core';
import { MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';

import { AddRefusal, finishAdd, isEditExisting, refusalOf, ruleSummary, SettledCreate } from './add-outcome';
import { I18nService } from '../../core/services/i18n.service';

describe('add-outcome', () => {
  const i18n = {
    instant: (k: string, p?: Record<string, unknown>) => (p ? `${k} ${JSON.stringify(p)}` : k),
    plural: (k: string) => k,
  } as unknown as I18nService;

  let dialogRef: { close: jest.Mock };
  let snackBar: { open: jest.Mock };
  let refusal: ReturnType<typeof signal<AddRefusal | null>>;

  const takeover = (existingUid?: number) => ({
    error: { existingUid, error: 'Another alarm of this type already uses those settings.', trackingType: 'pokemon' },
  });

  function finish(results: SettledCreate[], lookup?: (uid: number, type: null | string) => null | string): void {
    finishAdd(results, {
      dialogRef: dialogRef as unknown as MatDialogRef<unknown>,
      failedKey: 'ALARM.FAILED_CREATE',
      i18n,
      lookup,
      refusal,
      snackBar: snackBar as unknown as MatSnackBar,
    });
  }

  beforeEach(() => {
    dialogRef = { close: jest.fn() };
    snackBar = { open: jest.fn() };
    refusal = signal<AddRefusal | null>(null);
  });

  describe('finishAdd', () => {
    it('keeps a refused single Add open and sets the refusal instead of toasting', () => {
      finish([{ failed: takeover() }]);

      expect(dialogRef.close).not.toHaveBeenCalled();
      expect(snackBar.open).not.toHaveBeenCalled();
      expect(refusal()).toEqual({ existing: null, message: 'Another alarm of this type already uses those settings.' });
    });

    it('names the clashing rule when the lookup knows it', () => {
      const lookup = jest.fn().mockReturnValue('Snorlax · Within 2 km of my pin');

      finish([{ failed: takeover(77) }], lookup);

      expect(lookup).toHaveBeenCalledWith(77, 'pokemon');
      expect(refusal()?.existing).toEqual({ uid: 77, summary: 'Snorlax · Within 2 km of my pin', trackingType: 'pokemon' });
    });

    it('offers no rule when the lookup cannot find it', () => {
      finish([{ failed: takeover(77) }], () => null);

      expect(refusal()?.existing).toBeNull();
    });

    it('shows the first validation message of an ASP.NET 400', () => {
      finish([
        {
          failed: {
            error: { errors: { MinIv: ['MinIv must be between 0 and 100.'] }, title: 'One or more validation errors occurred.' },
            status: 400,
          },
        },
      ]);

      expect(refusal()?.message).toBe('MinIv must be between 0 and 100.');
    });

    it('says to wait on a 429, which is not a problem with the settings', () => {
      finish([{ failed: { error: null, status: 429 } }]);

      expect(refusal()?.message).toBe('ALARM.FAILED_TOO_MANY');
    });

    it('toasts the same words for a refused row in a batch', () => {
      finish([{ uid: 501 }, { failed: { error: { errors: { Distance: ['Distance is too large.'] } }, status: 400 } }]);

      expect(snackBar.open).toHaveBeenCalledWith('Distance is too large.', 'COMMON.OK', expect.anything());
    });

    it('falls back to the dialog message when the server gave no reason', () => {
      finish([{ failed: new Error('network') }]);

      expect(refusal()?.message).toBe('ALARM.FAILED_CREATE');
    });

    /**
     * The error interceptor already toasts these statuses. An inline note on top of that said the same
     * thing twice; the dialog stays open with the user's settings and the toast is the one message.
     */
    it.each([0, 403, 404, 500, 502, 503, 504])('keeps a single Add open without an inline note for a %i the toast reports', status => {
      finish([{ failed: { error: { error: 'PoracleNG could not be reached.' }, status } }]);

      expect(dialogRef.close).not.toHaveBeenCalled();
      expect(snackBar.open).not.toHaveBeenCalled();
      expect(refusal()).toBeNull();
    });

    it.each([400, 409, 422])('still shows a %i refusal inline, which no toast reports', status => {
      finish([{ failed: { error: { error: 'Another alarm of this type already uses those settings.' }, status } }]);

      expect(refusal()?.message).toBe('Another alarm of this type already uses those settings.');
    });

    it('closes a successful single Add', () => {
      finish([{ uid: 501 }]);

      expect(refusal()).toBeNull();
      expect(snackBar.open).toHaveBeenCalledWith('ALARM.SNACK_CREATED', 'COMMON.OK', expect.anything());
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('says a single Add already stored exactly was left as it is', () => {
      finish([{ uid: 0 }]);

      expect(snackBar.open).toHaveBeenCalledWith('ALARM.ALREADY_TRACKED_ONE', 'COMMON.OK', expect.anything());
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('closes a batch with a refusal in it as before, toasting the first reason', () => {
      finish([{ uid: 501 }, { failed: takeover(77) }, { uid: 0 }]);

      expect(refusal()).toBeNull();
      expect(snackBar.open).toHaveBeenCalledWith('Another alarm of this type already uses those settings.', 'COMMON.OK', expect.anything());
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    /**
     * A batch row that failed on a 5xx was already toasted by the interceptor ("Server error"). Following it
     * with "Check the settings and try again" sent the user to their filters for an outage.
     */
    it.each([0, 500, 503])('leaves the interceptor toast alone when a batch row failed on a %i', status => {
      finish([{ uid: 501 }, { failed: { error: { error: 'PoracleNG could not be reached.' }, status } }]);

      expect(snackBar.open).not.toHaveBeenCalled();
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('still toasts a refusal no toast reported when another row in the batch failed on a 5xx', () => {
      finish([{ failed: { error: null, status: 503 } }, { failed: takeover(77) }]);

      expect(snackBar.open).toHaveBeenCalledTimes(1);
      expect(snackBar.open).toHaveBeenCalledWith('Another alarm of this type already uses those settings.', 'COMMON.OK', expect.anything());
    });

    it('closes a batch that was refused entirely, as before', () => {
      finish([{ failed: takeover() }, { failed: takeover() }]);

      expect(refusal()).toBeNull();
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('counts a batch with an already-tracked row the way it always has', () => {
      finish([{ uid: 501 }, { uid: 0 }]);

      expect(snackBar.open).toHaveBeenCalledWith(
        'ALARM.SNACK_CREATED_WITH_DUPLICATES {"count":1,"duplicates":1}',
        'COMMON.OK',
        expect.anything(),
      );
    });
  });

  describe('refusalOf', () => {
    it('ignores an existingUid that is not a positive number', () => {
      const lookup = jest.fn().mockReturnValue('x');

      expect(refusalOf({ error: { existingUid: '77', error: 'no' } }, 'fallback', lookup)?.existing).toBeNull();
      expect(refusalOf({ error: { existingUid: 0, error: 'no' } }, 'fallback', lookup)?.existing).toBeNull();
      expect(lookup).not.toHaveBeenCalled();
    });
  });

  describe('isEditExisting', () => {
    it('recognises only a close result naming a rule', () => {
      expect(isEditExisting({ editExisting: { uid: 4, trackingType: 'raid' } })).toBe(true);
      expect(isEditExisting(true)).toBe(false);
      expect(isEditExisting(undefined)).toBe(false);
      expect(isEditExisting({ editExisting: { uid: 0 } })).toBe(false);
    });
  });

  describe('ruleSummary', () => {
    const translate = { instant: (k: string, p?: Record<string, unknown>) => (p ? `${k}:${JSON.stringify(p)}` : k) } as TranslateService;

    it('puts what the rule is about before where it reaches you', () => {
      expect(ruleSummary('Snorlax', { distance: 2000 }, [], translate)).toBe('Snorlax · WHERE.NEAR_PIN:{"distance":"2"}');
    });

    it('describes the inherited scope by the profile areas', () => {
      expect(ruleSummary('Mossy Lure', { overrideAreas: [], distance: 0 }, ['terrigal'], translate)).toBe(
        'Mossy Lure · WHERE.PROFILE_AREAS',
      );
    });
  });
});
