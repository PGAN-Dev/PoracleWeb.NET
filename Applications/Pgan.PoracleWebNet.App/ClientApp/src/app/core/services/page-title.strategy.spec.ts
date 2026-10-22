import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';

import { PageTitleStrategy } from './page-title.strategy';
import { SettingsService } from './settings.service';
import { routes } from '../../app.routes';

@Component({ template: '' })
class BlankComponent {}

describe('PageTitleStrategy', () => {
  const setup = async (settings: Record<string, string> = {}) => {
    const siteSettings = signal(settings);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { component: BlankComponent, path: 'pokemon', title: 'NAV.POKEMON' },
          { component: BlankComponent, path: 'untitled' },
          { component: BlankComponent, data: { adminPage: true }, path: 'admin/areas', title: 'NAV.ADMIN_AREAS' },
        ]),
        provideTranslateService(),
        { provide: SettingsService, useValue: { siteSettings } },
        { provide: TitleStrategy, useClass: PageTitleStrategy },
      ],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { NAV: { ADMIN_AREAS: 'Areas', ADMIN_PAGE_TITLE: 'Admin: {{page}}', POKEMON: 'Pokémon' } });
    translate.setTranslation('de', { NAV: { ADMIN_AREAS: 'Gebiete', ADMIN_PAGE_TITLE: 'Admin: {{page}} (de)', POKEMON: 'Pokémon (de)' } });
    translate.use('en');
    const router = TestBed.inject(Router);
    const title = TestBed.inject(Title);
    const go = async (url: string) => {
      await router.navigateByUrl(url);
      TestBed.tick();
      return title.getTitle();
    };
    return { go, siteSettings, translate };
  };

  it('names the page, then the default site title', async () => {
    const { go } = await setup();

    expect(await go('/pokemon')).toBe('Pokémon · DM Alerts');
  });

  it("keeps the operator's custom_title as the suffix", async () => {
    const { go } = await setup({ custom_title: 'PoGO Alerts' });

    expect(await go('/pokemon')).toBe('Pokémon · PoGO Alerts');
  });

  it('follows custom_title when the settings land after navigation', async () => {
    const { go, siteSettings } = await setup();
    await go('/pokemon');

    siteSettings.set({ custom_title: 'PoGO Alerts' });
    TestBed.tick();

    expect(TestBed.inject(Title).getTitle()).toBe('Pokémon · PoGO Alerts');
  });

  it('follows the display language', async () => {
    const { go, translate } = await setup();
    await go('/pokemon');

    translate.use('de');
    TestBed.tick();

    expect(TestBed.inject(Title).getTitle()).toBe('Pokémon (de) · DM Alerts');
  });

  it('shows the site title alone on a route with no title', async () => {
    const { go } = await setup({ custom_title: 'PoGO Alerts' });

    expect(await go('/untitled')).toBe('PoGO Alerts');
  });

  // "Areas" on its own read the same as the user's Areas & Places tab.
  it('marks an admin page as admin', async () => {
    const { go } = await setup();

    expect(await go('/admin/areas')).toBe('Admin: Areas · DM Alerts');
  });

  it('translates the admin prefix with the page', async () => {
    const { go, translate } = await setup();
    await go('/admin/areas');

    translate.use('de');
    TestBed.tick();

    expect(TestBed.inject(Title).getTitle()).toBe('Admin: Gebiete (de) · DM Alerts');
  });

  it('marks every admin route, and only those', () => {
    const marked = routes.filter(r => r.data?.['adminPage']).map(r => r.path);
    const admin = routes.filter(r => r.path?.startsWith('admin/') && r.loadComponent).map(r => r.path);

    expect(marked).toEqual(admin);
  });

  it('gives every signed-in page a title', () => {
    const missing = routes
      .filter(r => r.loadComponent && !r.path?.startsWith('auth/') && r.path !== 'login')
      .filter(r => typeof r.title !== 'string')
      .map(r => r.path);

    expect(missing).toEqual([]);
  });
});
