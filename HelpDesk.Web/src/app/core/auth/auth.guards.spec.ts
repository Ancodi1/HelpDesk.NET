import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  provideRouter,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import { authGuard, roleGuard } from './auth.guards';
import { AuthService } from './auth.service';
import { signIn } from './auth.testing';
import { UserRole } from './auth.models';

describe('Functional auth guards', () => {
  let auth: AuthService;
  let http: HttpTestingController;
  const run = (guard: CanActivateFn) =>
    TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    auth.logout();
    http.verify();
  });
  it('redirects an anonymous visitor to login', () => {
    expect((run(authGuard) as UrlTree).toString()).toBe('/login');
    expect((run(roleGuard('Admin')) as UrlTree).toString()).toBe('/login');
  });
  it.each<UserRole>(['User', 'Technician', 'Admin'])(
    'allows an authenticated %s to visit the private session page',
    (role) => {
      signIn(auth, http, role);
      expect(run(authGuard)).toBe(true);
    },
  );
  it('permits the allowed role and redirects other authenticated roles', () => {
    signIn(auth, http, 'User');
    expect((run(roleGuard('Admin')) as UrlTree).toString()).toBe('/session');
    signIn(auth, http, 'Admin');
    expect(run(roleGuard('Admin'))).toBe(true);
  });
});
