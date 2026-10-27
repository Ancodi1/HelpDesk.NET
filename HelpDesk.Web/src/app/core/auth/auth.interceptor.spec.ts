import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { signIn } from './auth.testing';

describe('Authentication interceptor boundaries', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let auth: AuthService;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: { navigateByUrl: vi.fn().mockResolvedValue(true) } },
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });
  afterEach(() => {
    auth.logout();
    http.verify();
  });
  it.each(['/api/users/3', '/api/tickets?page=1'])(
    'attaches the token only on a protected API request',
    (url) => {
      const response = signIn(auth, http);
      client.get(url).subscribe();
      const request = http.expectOne(url);
      expect(request.request.headers.get('Authorization')).toBe('Bearer ' + response.accessToken);
      expect(request.request.redirect).toBe('error');
      request.flush({});
    },
  );
  it.each([
    '/api/health',
    '/api/auth/login',
    '/api/auth/register',
    'https://example.invalid/api/users',
    '//example.invalid/api/users',
    '/assets/icon.svg',
    '/api-docs/users',
    '/api/users/../../../external',
    '/api/users/..%2f..%2fexternal',
    '/api/\\example.invalid/users',
  ])('does not add credentials outside the protected API boundary: %s', (url) => {
    signIn(auth, http);
    client.get(url).subscribe();
    const request = http.expectOne(url);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });
  it('does not add credentials without a session', () => {
    client.get('/api/users/3').subscribe();
    const request = http.expectOne('/api/users/3');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });
  it('invalidates the current session on authenticated 401', () => {
    signIn(auth, http);
    client.get('/api/users/3').subscribe({ error: () => undefined });
    http.expectOne('/api/users/3').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(auth.user()).toBeNull();
    expect(auth.notice()).toBe('unauthorized');
  });
  it.each([403, 409, 500, 0])('does not erase the session on status %s', (status) => {
    signIn(auth, http);
    client.get('/api/users/3').subscribe({ error: () => undefined });
    const request = http.expectOne('/api/users/3');
    if (status === 0) request.error(new ProgressEvent('error'));
    else request.flush({}, { status, statusText: 'Error' });
    expect(auth.isAuthenticated()).toBe(true);
  });
  it('does not invalidate the session on a public endpoint 401', () => {
    signIn(auth, http);
    client.get('/api/health').subscribe({ error: () => undefined });
    http.expectOne('/api/health').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(auth.isAuthenticated()).toBe(true);
  });
  it('does not let a late 401 erase a new login', () => {
    signIn(auth, http);
    client.get('/api/users/3').subscribe({ error: () => undefined });
    const old = http.expectOne('/api/users/3');
    signIn(auth, http, 'User');
    old.flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(auth.user()?.role).toBe('User');
  });
});
