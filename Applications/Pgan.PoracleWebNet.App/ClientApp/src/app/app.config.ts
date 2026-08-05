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
    provideTranslateService({
      fallbackLang: 'en',
      loader: {
        provide: TranslateLoader,
        useClass: TranslateHttpLoader,
      },
    }),
    provideTranslateHttpLoader({ prefix: './assets/i18n/', suffix: '.json' }),
  ],
};
