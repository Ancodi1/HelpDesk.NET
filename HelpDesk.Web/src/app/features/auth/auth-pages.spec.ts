import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';
import { AuthService } from '../../core/auth/auth.service';
import { loginResponse } from '../../core/auth/auth.testing';
import { Login } from './login/login';
import { Register } from './register/register';
import { authError } from './auth.errors';

describe('Authentication pages', () => {
  let http: HttpTestingController;
  let auth: AuthService;
  let router: Router;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });
  afterEach(() => {
    auth.logout();
    http.verify();
  });
  it('shows required login errors without a request', () => {
    const f = TestBed.createComponent(Login);
    f.detectChanges();
    f.componentInstance.submit();
    f.detectChanges();
    expect(f.nativeElement.querySelectorAll('.field-error').length).toBe(2);
    expect(f.nativeElement.querySelector('input[type="password"]').autocomplete).toBe(
      'current-password',
    );
    http.expectNone('/api/auth/login');
  });
  it('submits once, clears the password and navigates after login', () => {
    const f = TestBed.createComponent(Login);
    const page = f.componentInstance;
    page.form.setValue({ email: 'person@example.invalid', password: 'test-only' });
    page.submit();
    page.submit();
    f.detectChanges();
    expect(f.nativeElement.querySelector('button').disabled).toBe(true);
    http.expectOne('/api/auth/login').flush(loginResponse());
    expect(page.form.controls.password.value).toBe('');
    expect(auth.isAuthenticated()).toBe(true);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/session');
  });
  it('sanitizes a 401 and allows retry', () => {
    const f = TestBed.createComponent(Login);
    const page = f.componentInstance;
    page.form.setValue({ email: 'person@example.invalid', password: 'test-only' });
    page.submit();
    http
      .expectOne('/api/auth/login')
      .flush({ error: 'do-not-echo' }, { status: 401, statusText: 'Unauthorized' });
    f.detectChanges();
    expect(f.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Correo o contraseña incorrectos',
    );
    expect(f.nativeElement.textContent).not.toContain('do-not-echo');
    expect(page.form.controls.password.value).toBe('');
    expect(page.pending()).toBe(false);
    page.form.controls.password.setValue('second-test-only');
    page.submit();
    http.expectOne('/api/auth/login').flush(loginResponse());
    expect(auth.isAuthenticated()).toBe(true);
  });
  it('cancels a login and clears the password on leaving the page', () => {
    const f = TestBed.createComponent(Login);
    const page = f.componentInstance;
    page.form.setValue({ email: 'person@example.invalid', password: 'test-only' });
    page.submit();
    const request = http.expectOne('/api/auth/login');
    f.destroy();
    expect(request.cancelled).toBe(true);
    expect(page.form.controls.password.value).toBe('');
    expect(auth.user()).toBeNull();
  });
  it('validates registration fields without creating users', () => {
    const f = TestBed.createComponent(Register);
    const page = f.componentInstance;
    page.form.setValue({ name: ' ', email: 'invalid', password: 'short' });
    page.submit();
    f.detectChanges();
    expect(f.nativeElement.querySelectorAll('.field-error').length).toBe(3);
    expect(f.nativeElement.querySelector('input[type="password"]').autocomplete).toBe(
      'new-password',
    );
    expect(page.form.contains('role')).toBe(false);
    http.expectNone('/api/auth/register');
    page.form.controls.name.setValue('n'.repeat(101));
    expect(page.form.controls.name.invalid).toBe(true);
  });
  it('registers once and returns to login without a token', () => {
    const page = TestBed.createComponent(Register).componentInstance;
    page.form.setValue({
      name: 'Test person',
      email: 'person@example.invalid',
      password: 'unit-test-only-value',
    });
    page.submit();
    page.submit();
    http
      .expectOne('/api/auth/register')
      .flush(
        {
          id: 4,
          name: 'Test person',
          email: 'person@example.invalid',
          role: 'User',
          createdAt: new Date().toISOString(),
        },
        { status: 201, statusText: 'Created' },
      );
    expect(auth.isAuthenticated()).toBe(false);
    expect(page.form.controls.password.value).toBe('');
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { registered: '1' } });
  });
  it('handles a registration conflict without leaking server text', () => {
    const f = TestBed.createComponent(Register);
    const page = f.componentInstance;
    page.form.setValue({
      name: 'Test person',
      email: 'person@example.invalid',
      password: 'unit-test-only-value',
    });
    page.submit();
    http
      .expectOne('/api/auth/register')
      .flush({ error: 'do-not-echo' }, { status: 409, statusText: 'Conflict' });
    f.detectChanges();
    expect(f.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Ya existe una cuenta',
    );
    expect(f.nativeElement.textContent).not.toContain('do-not-echo');
    expect(page.pending()).toBe(false);
  });
  it('does not apply registration length to the login form', () => {
    const page = TestBed.createComponent(Login).componentInstance;
    page.form.setValue({ email: 'person@example.invalid', password: 'short' });
    expect(page.form.valid).toBe(true);
  });
  it.each([401, 403, 409, 0, 500])('distinguishes safe messages for HTTP %s', (status) => {
    const message = authError(
      new HttpErrorResponse({ status, error: { error: 'do-not-echo' } }),
      'register',
    );
    expect(message).not.toContain('do-not-echo');
    expect(message).toContain(
      status === 401
        ? 'autorizar'
        : status === 403
          ? 'permiso'
          : status === 409
            ? 'Ya existe'
            : status === 0
              ? 'conectar'
              : 'completar',
    );
  });
});
