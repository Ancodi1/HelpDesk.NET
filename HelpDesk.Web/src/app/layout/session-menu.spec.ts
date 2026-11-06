import { DemoService } from '../core/demo/demo.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';
import { SessionMenu } from './session-menu';
import { AppLayout } from './app-layout';
import { AuthService } from '../core/auth/auth.service';
import { UserRole, roleNavigation } from '../core/auth/auth.models';
import { signIn } from '../core/auth/auth.testing';

describe('Session navigation', () => {
  let auth: AuthService;
  let http: HttpTestingController;
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
  it('offers login and registration to anonymous visitors', () => {
    const f = TestBed.createComponent(SessionMenu);
    f.detectChanges();
    expect(f.nativeElement.querySelector('a[href="/login"]')).not.toBeNull();
    expect(f.nativeElement.querySelector('a[href="/register"]')).not.toBeNull();
    expect(f.nativeElement.querySelector('button')).toBeNull();
  });
  it('identifies the shared demo on every page and closes the registration link', () => {
    TestBed.inject(DemoService).enabled.set(true);
    const f = TestBed.createComponent(AppLayout);
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Demo pública · Datos ficticios');
    expect(f.nativeElement.textContent).toContain('No introduzcas datos personales');
    expect(f.nativeElement.querySelector('a[href="/register"]')).toBeNull();
  });
  it('warns against personal data when demo configuration is unavailable', () => {
    TestBed.inject(DemoService).unavailable.set(true);
    const f = TestBed.createComponent(AppLayout);
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('No se pudo comprobar el modo de demostración');
  });
  it.each<UserRole>(['User', 'Technician', 'Admin'])(
    'adapts private navigation for %s without unfinished panels',
    (role) => {
      signIn(auth, http, role);
      const f = TestBed.createComponent(AppLayout);
      f.detectChanges();
      expect(f.nativeElement.textContent).toContain(roleNavigation[role]);
      expect(f.nativeElement.querySelector('a[href="/session"]')).not.toBeNull();
      const adminLink = f.nativeElement.querySelector('a[href="/admin"]');
      if (role === 'Admin') expect(adminLink).not.toBeNull();
      else expect(adminLink).toBeNull();
      expect(f.nativeElement.querySelector('a[href="/tickets"]')).not.toBeNull();
    },
  );
  it('closes the session locally and navigates to login', () => {
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    signIn(auth, http);
    const f = TestBed.createComponent(SessionMenu);
    f.detectChanges();
    f.nativeElement.querySelector('button').click();
    f.detectChanges();
    expect(auth.user()).toBeNull();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
    expect(f.nativeElement.querySelector('a[href="/login"]')).not.toBeNull();
  });
});
