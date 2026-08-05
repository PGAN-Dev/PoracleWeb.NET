import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { provideTranslateService, TranslateLoader } from '@ngx-translate/core';
import { TranslateHttpLoader, provideTranslateHttpLoader } from '@ngx-translate/http-loader';

import { routes } from './app.routes';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { oidcRefreshInterceptor } from './core/interceptors/oidc-refresh.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    // oidcRefreshInterceptor sits closest to the backend so it catches a 401 (and can silently
    // refresh + retry) before errorInterceptor redirects to the login page.
    provideHttpClient(withInterceptors([authInterceptor, errorInterceptor, oidcRefreshInterceptor])),
    provideAnimationsAsync(),
    // No `fallbackLang` here on purpose. ngx-translate v18 loads the fallback language eagerly
    // from inside the TranslateService constructor, which resolves TranslateLoader while the
    // injector is still building TranslateService -- a circular dependency that fails with
    // NG0200 ("error loading translation for en"). Only the fallback language is affected, so
    // English silently rendered raw keys while every other locale, loaded later via use(),
    // worked fine. v17's `defaultLanguage` did not load eagerly, which is why this only
    // appeared after the v18 upgrade.
    //
    // I18nService.init() calls setFallbackLang('en') instead, after bootstrap, where the
    // injector is complete and the loader resolves normally.
    provideTranslateService({
      loader: {
        provide: TranslateLoader,
        useClass: TranslateHttpLoader,
      },
    }),
    provideTranslateHttpLoader({ prefix: './assets/i18n/', suffix: '.json' }),
  ],
};
