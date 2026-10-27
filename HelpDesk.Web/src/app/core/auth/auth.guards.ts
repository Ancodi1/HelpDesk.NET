import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { UserRole } from './auth.models';

export const authGuard: CanActivateFn = () => {
  return inject(AuthService).isAuthenticated() || inject(Router).parseUrl('/login');
};

// Prepared for later role-specific pages; it never replaces server authorization.
export function roleGuard(...allowed: UserRole[]): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (!auth.isAuthenticated()) return router.parseUrl('/login');
    return allowed.some((role) => role === auth.user()?.role) || router.parseUrl('/session');
  };
}
