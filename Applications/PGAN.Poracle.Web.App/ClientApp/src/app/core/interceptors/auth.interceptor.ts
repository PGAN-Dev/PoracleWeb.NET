import { HttpInterceptorFn } from '@angular/common/http';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  // Don't add auth headers to external URLs (e.g. GitHub raw content)
  if (!req.url.startsWith('/') && !req.url.includes(location.host)) {
    return next(req);
  }

  const token = localStorage.getItem('poracle_token');

  if (token?.trim()) {
    req = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`,
      },
    });
  }

  return next(req);
};
