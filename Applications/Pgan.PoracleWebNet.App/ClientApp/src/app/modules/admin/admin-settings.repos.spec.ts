import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AdminSettingsComponent } from './admin-settings.component';
import { SettingsService } from '../../core/services/settings.service';
import { DEFAULT_ICON_REPOS, IconRepo, serializeIconRepos } from '../../shared/utils/icon-repos';

/**
 * The pack list used to be a hardcoded array, so removing a repository that had been deleted from
 * GitHub -- and every instance still pointing at it -- needed a release. It is now the `icon_repos`
 * setting. See #877.
 */
describe('AdminSettingsComponent icon repositories', () => {
  const dialogResult = { value: undefined as IconRepo | boolean | undefined };
  let sut: AdminSettingsComponent;

  const settingsFor = (values: Record<string, string>) => Object.entries(values).map(([key, value]) => ({ key, value }));

  const create = (values: Record<string, string>) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        {
          provide: SettingsService,
          useValue: {
            getOidcConfig: () => of(null),
            getAll: () => of(settingsFor(values)),
            getDiscordConfig: () => of(null),
            getTelegramConfig: () => of(null),
            isForcedByPoracle: () => false,
            siteSettings: () => values,
            update: () => of({}),
          },
        },
      ],
      imports: [AdminSettingsComponent, NoopAnimationsModule],
    });
    // MatDialogModule, imported by the component, provides the real MatDialog into the same injector,
    // so a provider entry above would lose to it. Override after configuring.
    TestBed.overrideProvider(MatDialog, { useValue: { open: () => ({ afterClosed: () => of(dialogResult.value) }) } });

    const fixture = TestBed.createComponent(AdminSettingsComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  };

  beforeEach(() => {
    dialogResult.value = undefined;
  });

  it('offers the built-in list on an instance that has never edited it', () => {
    sut = create({});

    expect(sut.iconRepos().map(r => r.base)).toEqual(DEFAULT_ICON_REPOS.map(r => r.base));
  });

  it('offers the stored list once one exists', () => {
    const stored: IconRepo[] = [{ name: 'Ours', base: 'https://ours.test/UICONS' }];
    sut = create({ icon_repos: serializeIconRepos(stored) });

    expect(sut.iconRepos()).toEqual(stored);
  });

  it('marks the pack the settings actually name', () => {
    sut = create({ uicons_pkmn: 'https://raw.githubusercontent.com/jms412/PkmnHomeIcons/master/UICONS/pokemon' });

    const active = sut.displayedRepos().filter(r => sut.isRepoActive(r));

    expect(active.map(r => r.name)).toEqual(['Jms412 (Home)']);
  });

  it('marks one pack active when another pack URL is a prefix of it', () => {
    // The old check asked whether the Pokemon base started with the card's base, so a list holding
    // `.../UICONS` and `.../UICONS-Shuffle` lit up both cards.
    const stored: IconRepo[] = [
      { name: 'Plain', base: 'https://a.test/UICONS' },
      { name: 'Shuffle', base: 'https://a.test/UICONS-Shuffle' },
    ];
    sut = create({ icon_repos: serializeIconRepos(stored), uicons_pkmn: 'https://a.test/UICONS-Shuffle/pokemon' });

    expect(
      sut
        .displayedRepos()
        .filter(r => sut.isRepoActive(r))
        .map(r => r.name),
    ).toEqual(['Shuffle']);
  });

  it('shows the configured pack even when nobody listed it', () => {
    // Otherwise an instance pointed by hand at its own pack shows no active card at all, which reads
    // as "nothing is configured" on a site whose icons are working.
    sut = create({ icon_repos: '[]', uicons_pkmn: 'https://ours.test/UICONS/pokemon' });

    const shown = sut.displayedRepos();

    expect(shown).toHaveLength(1);
    expect(shown[0].listed).toBe(false);
    expect(sut.isRepoActive(shown[0])).toBe(true);
  });

  it('does not show the configured pack twice when it is in the list', () => {
    const stored: IconRepo[] = [{ name: 'Ours', base: 'https://ours.test/UICONS' }];
    sut = create({ icon_repos: serializeIconRepos(stored), uicons_pkmn: 'https://ours.test/UICONS/pokemon' });

    expect(sut.displayedRepos()).toHaveLength(1);
  });

  it('shows nothing rather than the defaults once every entry is removed', () => {
    sut = create({ icon_repos: '[]' });

    expect(sut.displayedRepos()).toEqual([]);
  });

  it('adds a pack to the list', () => {
    sut = create({ icon_repos: '[]' });
    dialogResult.value = { name: 'New', base: 'https://new.test/UICONS' };

    sut.addRepo();

    expect(sut.iconRepos()).toEqual([{ name: 'New', base: 'https://new.test/UICONS' }]);
  });

  it('replaces an entry in place when editing it, rather than appending', () => {
    const stored: IconRepo[] = [
      { name: 'A', base: 'https://a.test/UICONS' },
      { name: 'B', base: 'https://b.test/UICONS' },
    ];
    sut = create({ icon_repos: serializeIconRepos(stored) });
    dialogResult.value = { name: 'A, renamed', base: 'https://a.test/UICONS' };

    sut.addRepo(stored[0]);

    expect(sut.iconRepos()).toEqual([{ name: 'A, renamed', base: 'https://a.test/UICONS' }, stored[1]]);
  });

  it('removes a pack from the list and leaves the icon settings alone', () => {
    // Clearing uicons_* here would blank every icon on the site as a side effect of tidying a menu.
    const stored: IconRepo[] = [{ name: 'Ours', base: 'https://ours.test/UICONS' }];
    sut = create({ icon_repos: serializeIconRepos(stored), uicons_pkmn: 'https://ours.test/UICONS/pokemon' });
    dialogResult.value = true;

    sut.removeRepo(stored[0]);

    expect(sut.iconRepos()).toEqual([]);
    expect(sut.getSettingValue('uicons_pkmn')).toBe('https://ours.test/UICONS/pokemon');
  });

  it('keeps the pack when the confirmation is declined', () => {
    const stored: IconRepo[] = [{ name: 'Ours', base: 'https://ours.test/UICONS' }];
    sut = create({ icon_repos: serializeIconRepos(stored) });
    dialogResult.value = false;

    sut.removeRepo(stored[0]);

    expect(sut.iconRepos()).toEqual(stored);
  });

  it('puts the built-in list back', () => {
    sut = create({ icon_repos: '[]' });

    sut.restoreDefaultRepos();

    expect(sut.iconRepos()).toEqual(DEFAULT_ICON_REPOS.map(r => ({ ...r })));
  });

  it('stages list edits rather than saving them, like picking a pack does', () => {
    sut = create({ icon_repos: '[]' });
    dialogResult.value = { name: 'New', base: 'https://new.test/UICONS' };

    sut.addRepo();

    expect(sut.modifiedSettings().has('icon_repos')).toBe(true);
  });
});
