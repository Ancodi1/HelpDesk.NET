import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export function requiresAuthentication(url: string): boolean {
  // Only our relative API URLs; normalize dot segments before classifying the path.
  if (!url.startsWith('/api/') || url.includes('\\')) return false;
  const parsed = new URL(url, 'http://helpdesk.invalid');
  if (/%(?:2f|5c)/i.test(parsed.pathname)) return false;
  return (
    parsed.origin === 'http://helpdesk.invalid' &&
    /^\/api\/(tickets|users)(\/|$)/.test(parsed.pathname)
  );
}

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!requiresAuthentication(request.url)) return next(request);
  const auth = inject(AuthService);
  const authorization = auth.authorization();
  if (!authorization) return next(request);
  return next(
    request.clone({
      setHeaders: { Authorization: `Bearer ${authorization.token}` },
      // Do not forward an authenticated request through redirects to another origin.
      redirect: 'error',
    }),
  ).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401)
        auth.rejectSession(authorization.version);
      return throwError(() => error);
    }),
  );
};
