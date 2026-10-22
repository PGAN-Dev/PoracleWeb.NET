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
      failedKey: 'POKEMON.SNACK_FAILED_CREATE',
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

    it('falls back to the dialog message when the server gave no reason', () => {
      finish([{ failed: new Error('network') }]);

      expect(refusal()?.message).toBe('POKEMON.SNACK_FAILED_CREATE');
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

      expect(refusalOf({ error: { existingUid: '77', error: 'no' } }, 'fallback', lookup).existing).toBeNull();
      expect(refusalOf({ error: { existingUid: 0, error: 'no' } }, 'fallback', lookup).existing).toBeNull();
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
