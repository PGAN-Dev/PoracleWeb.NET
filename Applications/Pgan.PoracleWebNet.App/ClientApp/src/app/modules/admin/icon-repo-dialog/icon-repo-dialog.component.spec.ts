import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';

import { IconRepoDialogComponent, IconRepoDialogData } from './icon-repo-dialog.component';
import { IconPackProbeService } from '../../../core/services/icon-pack-probe.service';
import { ICON_SOURCE_KEYS } from '../../../core/services/icon.service';
import { IconRepo } from '../../../shared/utils/icon-repos';

describe('IconRepoDialogComponent', () => {
  const closed = jest.fn();
  let probe: jest.Mock;

  const create = (data: Partial<IconRepoDialogData> = {}) => {
    probe = jest.fn().mockResolvedValue({ base: '', missing: [] });
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MatDialogRef, useValue: { close: closed } },
        { provide: MAT_DIALOG_DATA, useValue: { existingBases: [], repo: null, ...data } },
        { provide: IconPackProbeService, useValue: { probe: (base: string) => probe(base) } },
      ],
      imports: [IconRepoDialogComponent, NoopAnimationsModule],
    });
    return TestBed.createComponent(IconRepoDialogComponent).componentInstance;
  };

  beforeEach(() => closed.mockClear());

  it('will not save a pack nobody has checked', () => {
    // The whole point of the check is that a URL which looks right and serves nothing is the failure
    // this feature exists to prevent. Allowing Add before it runs would let one straight in.
    const sut = create();
    sut.name.set('Mine');
    sut.base.set('https://a.test/UICONS');

    expect(sut.canSave()).toBe(false);
  });

  it('saves a checked pack, with the base normalized', async () => {
    const sut = create();
    sut.name.set('  Mine  ');
    sut.onBaseChanged('  https://a.test/UICONS/  ');
    probe.mockResolvedValue({ base: 'https://a.test/UICONS', missing: [] });
    await sut.check();

    expect(sut.canSave()).toBe(true);
    sut.save();

    expect(closed).toHaveBeenCalledWith({ name: 'Mine', base: 'https://a.test/UICONS' });
  });

  it('refuses a pack missing a category, and says which', async () => {
    const sut = create();
    sut.name.set('Partial');
    sut.onBaseChanged('https://a.test/UICONS');
    probe.mockResolvedValue({ base: 'https://a.test/UICONS', missing: ['uicons_type'] });
    await sut.check();

    expect(sut.canSave()).toBe(false);
    expect(sut.missingKeys()).toEqual(['uicons_type']);
  });

  it('throws away a verdict as soon as the URL changes', async () => {
    // Otherwise checking a good pack, then pasting a different URL, saves the second one on the
    // first one's evidence.
    const sut = create();
    sut.name.set('Mine');
    sut.onBaseChanged('https://good.test/UICONS');
    probe.mockResolvedValue({ base: 'https://good.test/UICONS', missing: [] });
    await sut.check();
    expect(sut.canSave()).toBe(true);

    sut.onBaseChanged('https://other.test/UICONS');

    expect(sut.canSave()).toBe(false);
  });

  it('ignores a verdict that arrives about a URL no longer in the box', async () => {
    const sut = create();
    sut.name.set('Mine');
    sut.onBaseChanged('https://slow.test/UICONS');
    probe.mockResolvedValue({ base: 'https://slow.test/UICONS', missing: [] });

    const pending = sut.check();
    sut.onBaseChanged('https://typed-more.test/UICONS');
    await pending;

    expect(sut.canSave()).toBe(false);
  });

  it('refuses a pack already in the list', async () => {
    const sut = create({ existingBases: ['https://a.test/UICONS'] });
    sut.name.set('Again');
    sut.onBaseChanged('https://a.test/UICONS/');
    probe.mockResolvedValue({ base: 'https://a.test/UICONS', missing: [] });
    await sut.check();

    expect(sut.duplicate()).toBe(true);
    expect(sut.canSave()).toBe(false);
  });

  it('does not call the entry being edited a duplicate of itself', () => {
    const repo: IconRepo = { name: 'Mine', base: 'https://a.test/UICONS' };
    const sut = create({ existingBases: [repo.base], repo });

    expect(sut.editing).toBe(true);
    expect(sut.duplicate()).toBe(false);
  });

  it('renames an entry already in the list without demanding a fresh check', () => {
    // The case worth protecting: a pack whose host is down for an hour should not also block fixing
    // a typo in its name.
    const repo: IconRepo = { name: 'Mine', base: 'https://a.test/UICONS' };
    const sut = create({ existingBases: [repo.base], repo });

    sut.name.set('Mine, renamed');

    expect(sut.canSave()).toBe(true);
    expect(probe).not.toHaveBeenCalled();
  });

  it('demands a check once a listed entry points somewhere new', () => {
    const repo: IconRepo = { name: 'Mine', base: 'https://a.test/UICONS' };
    const sut = create({ existingBases: [repo.base], repo });

    sut.onBaseChanged('https://somewhere-else.test/UICONS');

    expect(sut.canSave()).toBe(false);
  });

  /**
   * The unlisted "currently configured" card reaches this dialog through the same call as the pencil,
   * carrying a pack that is not in the list. Treating that as an edit marked it checked without
   * checking, and the dialog printed "Every category loaded" over five broken thumbnails -- about
   * whitewillem/PogoAssets, the deleted repository this whole feature exists because of.
   */
  describe('adding the pack this instance is configured to', () => {
    const configured: IconRepo = { name: 'raw.githubusercontent.com/main/uicons', base: 'https://dead.test/uicons' };

    it('is an add, not an edit', () => {
      const sut = create({ existingBases: ['https://other.test/UICONS'], repo: configured });

      expect(sut.editing).toBe(false);
    });

    it('will not save it unchecked', () => {
      const sut = create({ existingBases: ['https://other.test/UICONS'], repo: configured });
      sut.name.set('Ours');

      expect(sut.probeOk()).toBe(false);
      expect(sut.canSave()).toBe(false);
    });

    it('claims nothing about it until a probe has actually run', () => {
      const sut = create({ existingBases: ['https://other.test/UICONS'], repo: configured });

      expect(sut.probeState().kind).toBe('idle');
    });

    it('refuses it once the check reports the pack is gone', async () => {
      const sut = create({ existingBases: ['https://other.test/UICONS'], repo: configured });
      sut.name.set('Ours');
      probe.mockResolvedValue({ base: configured.base, missing: [...ICON_SOURCE_KEYS] });
      await sut.check();

      expect(sut.canSave()).toBe(false);
      expect(sut.missingKeys()).toEqual([...ICON_SOURCE_KEYS]);
    });

    it('takes the URL but not the derived label, which is display text rather than a name', () => {
      const sut = create({ existingBases: [], repo: configured });

      expect(sut.base()).toBe(configured.base);
      expect(sut.name()).toBe('');
    });
  });
});
