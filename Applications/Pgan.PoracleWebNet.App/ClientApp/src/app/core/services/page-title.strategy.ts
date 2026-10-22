import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { ActivatedRouteSnapshot, RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { map, of, switchMap } from 'rxjs';

import { DEFAULT_SITE_TITLE, SettingsService } from './settings.service';

interface PageTitle {
  /** `data: { adminPage: true }`: "Admin: Areas", so it isn't read as the user's Areas & Places. */
  admin: boolean;
  key: string | undefined;
}

/**
 * "Pokémon · DM Alerts". Every route used to carry the site title alone, so browser tabs, history and a
 * screen reader's page announcement could not tell one page from another (WCAG 2.4.2, UI audit cross D9).
 *
 * A route's `title` is an i18n key rather than text, so the page name follows the display language, and
 * the suffix is the operator's `custom_title`, which arrives with the settings after the first navigation.
 * A route with no title, or a key with no translation yet, shows the site title alone.
 */
@Injectable({ providedIn: 'root' })
export class PageTitleStrategy extends TitleStrategy {
  private readonly page = signal<PageTitle>({ admin: false, key: undefined });
  private readonly translate = inject(TranslateService);
  private readonly pageName = toSignal(
    toObservable(this.page).pipe(
      switchMap(({ admin, key }) => {
        if (!key) return of('');
        const name$ = this.translate.stream(key).pipe(map(text => (text === key ? '' : String(text))));
        if (!admin) return name$;
        return name$.pipe(
          switchMap(page => (page ? this.translate.stream('NAV.ADMIN_PAGE_TITLE', { page }).pipe(map(text => String(text))) : of(''))),
        );
      }),
    ),
    { initialValue: '' },
  );

  private readonly settings = inject(SettingsService);

  private readonly siteTitle = computed(() => this.settings.siteSettings()['custom_title'] || DEFAULT_SITE_TITLE);

  private readonly title = inject(Title);

  private readonly apply = effect(() => {
    const page = this.pageName();
    const site = this.siteTitle();
    this.title.setTitle(page ? `${page} · ${site}` : site);
  });

  override updateTitle(snapshot: RouterStateSnapshot): void {
    let route: ActivatedRouteSnapshot = snapshot.root;
    while (route.firstChild) route = route.firstChild;
    this.page.set({ admin: route.data['adminPage'] === true, key: this.buildTitle(snapshot) });
  }
}
