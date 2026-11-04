import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from '../../app';
import { routes } from '../../app.routes';
import { AuthService } from '../../core/auth/auth.service';
import { signIn } from '../../core/auth/auth.testing';
import { authInterceptor } from '../../core/auth/auth.interceptor';
import { pageFixture } from '../tickets/ticket.testing';
import { userFixture } from './user.testing';

describe('Administrative navigation with real guards and interceptor', () => {
  let http: HttpTestingController;
  let auth: AuthService;
  let router: Router;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });
  afterEach(() => {
    auth.logout();
    http.verify();
  });
  it.each(['User', 'Technician', null] as const)(
    'blocks %s before loading administrative data',
    async (role) => {
      if (role) signIn(auth, http, role);
      const f = TestBed.createComponent(App);
      await router.navigateByUrl('/admin');
      f.detectChanges();
      await f.whenStable();
      expect(router.url).toBe(role ? '/session' : '/login');
      expect(f.nativeElement.querySelector('a[href="/admin"]')).toBeNull();
      http.expectNone((r) => r.url === '/api/users' || r.url === '/api/tickets');
      f.destroy();
    },
  );
  async function openAdmin() {
    signIn(auth, http);
    const f = TestBed.createComponent(App);
    await router.navigateByUrl('/admin');
    f.detectChanges();
    await f.whenStable();
    const requests = http.match((r) => r.url === '/api/tickets');
    expect(requests).toHaveLength(4);
    for (const r of requests) {
      expect(r.request.headers.has('Authorization')).toBe(true);
      r.flush(pageFixture({ pageSize: 1 }));
    }
    return f;
  }
  it.each([401, 403])('handles administrative HTTP %s without login loops', async (status) => {
    const f = await openAdmin();
    http.expectOne('/api/users').flush({}, { status, statusText: 'Error' });
    await f.whenStable();
    f.detectChanges();
    expect(auth.isAuthenticated()).toBe(status === 403);
    expect(router.url).toBe(status === 401 ? '/login' : '/admin');
    http.expectNone('/api/users');
    f.destroy();
  });
  it('uses sidebar active state and intentionally ends a session after changing its own role', async () => {
    const f = await openAdmin();
    http.expectOne('/api/users').flush([userFixture({ id: 3, role: 'Admin' })]);
    f.detectChanges();
    const link = f.nativeElement.querySelector('a[href="/admin"]') as HTMLElement;
    expect(link.classList.contains('active')).toBe(true);
    expect(link.getAttribute('aria-current')).toBe('page');
    (f.nativeElement.querySelector('.users button') as HTMLButtonElement).click();
    f.detectChanges();
    const select = f.nativeElement.querySelector('#user-role') as HTMLSelectElement;
    select.value = 'Technician';
    select.dispatchEvent(new Event('change'));
    f.detectChanges();
    const form = f.nativeElement.querySelector('app-admin form') as HTMLFormElement;
    const event = new Event('submit', { bubbles: true, cancelable: true });
    form.dispatchEvent(event);
    f.detectChanges();
    expect(event.defaultPrevented).toBe(true);
    expect(f.nativeElement.textContent).toContain('último administrador');
    expect(f.nativeElement.textContent).toContain('tu propio rol');
    http.expectNone((r) => r.method === 'PUT');
    (f.nativeElement.querySelector('.confirmation .primary') as HTMLButtonElement).click();
    const request = http.expectOne('/api/users/3');
    expect(request.request.headers.has('Authorization')).toBe(true);
    request.flush(userFixture({ id: 3, role: 'Technician' }));
    await f.whenStable();
    f.detectChanges();
    expect(auth.isAuthenticated()).toBe(false);
    expect(router.url).toBe('/login');
    http.expectNone('/api/users');
    f.destroy();
  });
});
