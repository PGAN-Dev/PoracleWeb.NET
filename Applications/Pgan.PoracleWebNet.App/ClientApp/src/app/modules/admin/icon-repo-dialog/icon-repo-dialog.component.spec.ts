import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';

import { IconRepoDialogComponent, IconRepoDialogData } from './icon-repo-dialog.component';
import { IconPackProbeService } from '../../../core/services/icon-pack-probe.service';
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
    // An entry in the list was probed when it was added; re-probing on open would block fixing a
    // typo in the name while the pack's host happens to be down.
    expect(sut.canSave()).toBe(true);
  });
});
