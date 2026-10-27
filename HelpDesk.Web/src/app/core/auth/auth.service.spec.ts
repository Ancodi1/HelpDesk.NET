import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { AuthService } from './auth.service';
import { loginResponse, signIn } from './auth.testing';
import { UserRole } from './auth.models';

describe('AuthService in-memory session', () => {
  let auth: AuthService;
  let http: HttpTestingController;
  const navigate = vi.fn().mockResolvedValue(true);
  beforeEach(() => {
    navigate.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: { navigateByUrl: navigate } },
      ],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    auth.logout();
    http.verify();
    vi.useRealTimers();
  });

  it('starts with no persisted session', () => {
    expect(auth.user()).toBeNull();
    expect(auth.authorization()).toBeNull();
  });
  it.each<UserRole>(['User', 'Technician', 'Admin'])(
    'reads the server-issued %s role after login',
    (role) => {
      signIn(auth, http, role);
      expect(auth.isAuthenticated()).toBe(true);
      expect(auth.user()).toEqual({ id: 3, email: 'person@example.invalid', role });
    },
  );
  it('normalizes email but preserves the password exactly, including spaces', () => {
    auth.login({ email: ' PERSON@EXAMPLE.INVALID ', password: '  unit-test-only  ' }).subscribe();
    const request = http.expectOne('/api/auth/login');
    expect(request.request.body).toEqual({
      email: 'person@example.invalid',
      password: '  unit-test-only  ',
    });
    expect(request.request.redirect).toBe('error');
    request.flush(loginResponse());
  });
  it('registers only the real input fields and does not start a session', () => {
    auth
      .register({
        name: 'Test person',
        email: ' PERSON@EXAMPLE.INVALID ',
        password: 'unit-test-only-value',
      })
      .subscribe();
    const request = http.expectOne('/api/auth/register');
    expect(request.request.body).toEqual({
      name: 'Test person',
      email: 'person@example.invalid',
      password: 'unit-test-only-value',
    });
    request.flush(
      {
        id: 4,
        name: 'Test person',
        email: 'person@example.invalid',
        role: 'User',
        createdAt: new Date().toISOString(),
      },
      { status: 201, statusText: 'Created' },
    );
    expect(auth.user()).toBeNull();
  });
  it('clears the token, user and expiry on local logout', () => {
    signIn(auth, http);
    auth.logout();
    expect(auth.user()).toBeNull();
    expect(auth.authorization()).toBeNull();
    expect(auth.expiresAt()).toBeNull();
  });
  it('cannot restore a session from a pending login after logout', () => {
    const error = vi.fn();
    auth.login({ email: 'person@example.invalid', password: 'test-only' }).subscribe({ error });
    const request = http.expectOne('/api/auth/login');
    auth.logout();
    request.flush(loginResponse());
    expect(error).toHaveBeenCalledOnce();
    expect(auth.user()).toBeNull();
  });
  it('ignores a superseded login response', () => {
    const error = vi.fn();
    auth.login({ email: 'person@example.invalid', password: 'test-only' }).subscribe({ error });
    const earlier = http.expectOne('/api/auth/login');
    signIn(auth, http, 'Technician');
    earlier.flush(loginResponse('Admin'));
    expect(error).toHaveBeenCalledOnce();
    expect(auth.user()?.role).toBe('Technician');
  });
  it('expires automatically at the shorter expiresIn deadline', () => {
    vi.useFakeTimers();
    signIn(auth, http, 'User', {}, 2);
    vi.advanceTimersByTime(2001);
    expect(auth.user()).toBeNull();
    expect(auth.notice()).toBe('expired');
    expect(navigate).toHaveBeenCalledWith('/login');
  });
  it('uses exp when it is earlier than expiresIn, including after clock jumps', () => {
    vi.useFakeTimers();
    const now = Math.floor(Date.now() / 1000);
    signIn(auth, http, 'Admin', { exp: now + 2 });
    vi.setSystemTime(Date.now() + 3000);
    expect(auth.authorization()).toBeNull();
    expect(auth.notice()).toBe('expired');
  });
  it('a stale 401 does not close a newer session', () => {
    signIn(auth, http);
    const old = auth.authorization()!;
    signIn(auth, http, 'Technician');
    auth.rejectSession(old.version);
    expect(auth.user()?.role).toBe('Technician');
    auth.rejectSession(auth.authorization()!.version);
    expect(auth.user()).toBeNull();
    expect(auth.notice()).toBe('unauthorized');
  });
  it.each([
    { exp: 1 },
    { role: 'Owner' },
    { role: ['Admin'] },
    { sub: '-1' },
    { sub: '9007199254740992' },
    { email: '' },
    { nbf: 9999999999 },
  ])('rejects invalid or expired claims without a session', (claims) => {
    const error = vi.fn();
    auth.login({ email: 'person@example.invalid', password: 'test-only' }).subscribe({ error });
    http.expectOne('/api/auth/login').flush(loginResponse('Admin', claims));
    expect(error).toHaveBeenCalledOnce();
    expect(auth.user()).toBeNull();
  });
  it.each([{}, { tokenType: 'Other' }, { expiresIn: 0 }, { accessToken: 'invalid' }])(
    'rejects invalid login response contracts',
    (patch) => {
      const error = vi.fn();
      auth.login({ email: 'person@example.invalid', password: 'test-only' }).subscribe({ error });
      http
        .expectOne('/api/auth/login')
        .flush(Object.keys(patch).length ? { ...loginResponse(), ...patch } : {});
      expect(error).toHaveBeenCalledOnce();
      expect(auth.user()).toBeNull();
    },
  );
  it('never writes session credentials into browser storage', () => {
    const write = vi.spyOn(Storage.prototype, 'setItem');
    signIn(auth, http);
    auth.logout();
    expect(write).not.toHaveBeenCalled();
    write.mockRestore();
  });
  it('clears the old expiry timer when a new session starts', () => {
    vi.useFakeTimers();
    signIn(auth, http, 'Admin', {}, 1);
    signIn(auth, http, 'Technician', {}, 10);
    vi.advanceTimersByTime(2000);
    expect(auth.user()?.role).toBe('Technician');
    expect(auth.isAuthenticated()).toBe(true);
  });
  it('rejects malformed JWT payloads without keeping a token', () => {
    const error = vi.fn();
    auth.login({ email: 'person@example.invalid', password: 'test-only' }).subscribe({ error });
    http.expectOne('/api/auth/login').flush({ ...loginResponse(), accessToken: 'a.notjson.c' });
    expect(error).toHaveBeenCalledOnce();
    expect(auth.user()).toBeNull();
  });
  it('times out login without leaving a session', () => {
    vi.useFakeTimers();
    const error = vi.fn();
    auth.login({ email: 'person@example.invalid', password: 'test-only' }).subscribe({ error });
    const request = http.expectOne('/api/auth/login');
    vi.advanceTimersByTime(15001);
    expect(request.cancelled).toBe(true);
    expect(error).toHaveBeenCalledOnce();
    expect(auth.user()).toBeNull();
  });
});
