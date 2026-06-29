import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject, PLATFORM_ID } from '@angular/core';
import { isPlatformServer } from '@angular/common';

export const apiUrlInterceptor: HttpInterceptorFn = (req, next) => {
  const platformId = inject(PLATFORM_ID);

  if (isPlatformServer(platformId) && req.url.startsWith('/api/')) {
    const apiBaseUrl = process.env['API_BASE_URL'] ?? 'http://localhost:5000';
    const absoluteUrl = `${apiBaseUrl}${req.url}`;
    const serverReq = req.clone({ url: absoluteUrl });
    return next(serverReq);
  }

  return next(req);
};
