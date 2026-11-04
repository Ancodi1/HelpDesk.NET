import { provideHttpClient, HttpErrorResponse, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { App } from '../../app';
import { routes } from '../../app.routes';
import { AuthService } from '../../core/auth/auth.service';
import { authInterceptor } from '../../core/auth/auth.interceptor';
import { signIn } from '../../core/auth/auth.testing';
import { pageFixture } from '../tickets/ticket.testing';
import { Admin } from './admin';
import { userError } from './user.service';
import { userFixture } from './user.testing';

describe('Last administrator conflict in the administrative panel', () => {
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

  it.each(['User', 'Technician'] as const)(
    'keeps session and attempted %s form after 409, then reloads without auto-writing',
    async (role) => {
      signIn(auth, http);
      const f = TestBed.createComponent(App);
      try {
        await router.navigateByUrl('/admin');
        f.detectChanges();
        await f.whenStable();
        for (const request of http.match((r) => r.url === '/api/tickets'))
          request.flush(pageFixture({ pageSize: 1 }));
        const admin = userFixture({ id: 3, role: 'Admin' });
        http.expectOne('/api/users').flush([admin]);
        f.detectChanges();
        const c = f.debugElement.query(By.directive(Admin)).componentInstance as Admin;
        c.edit(admin);
        c.form.controls.role.setValue(role);
        c.save();
        f.detectChanges();
        expect(c.confirming()).toBe(true);
        expect(f.nativeElement.textContent).toContain('último administrador');
        http.expectNone((r) => r.method === 'PUT');
        (f.nativeElement.querySelector('.confirmation .primary') as HTMLButtonElement).click();
        c.save(true);
        const write = http.expectOne('/api/users/3');
        expect(write.request.body.role).toBe(role);
        write.flush(
          { code: 'last_admin', error: 'provider technical details' },
          { status: 409, statusText: 'Conflict' },
        );
        await f.whenStable();
        f.detectChanges();
        expect(auth.isAuthenticated()).toBe(true);
        expect(auth.user()?.role).toBe('Admin');
        expect(router.url).toBe('/admin');
        expect(c.selected()?.role).toBe('Admin');
        expect(c.form.controls.role.value).toBe(role);
        expect(c.confirming()).toBe(false);
        expect(c.reviewRequired()).toBe(true);
        expect(f.nativeElement.querySelector('fieldset').disabled).toBe(true);
        expect(f.nativeElement.textContent).toContain(
          'No se puede cambiar el rol del último administrador',
        );
        expect(f.nativeElement.textContent).not.toContain('provider technical details');
        c.save();
        c.save(true);
        http.expectNone((r) => r.method === 'PUT');
        c.reloadUsers();
        http.expectOne('/api/users').flush([admin]);
        f.detectChanges();
        expect(c.selected()).toBeNull();
        expect(c.reviewRequired()).toBe(false);
        c.edit(admin);
        c.form.controls.name.setValue('Updated name');
        c.save();
        http.expectOne('/api/users/3').flush({ ...admin, name: 'Updated name' });
        expect(auth.isAuthenticated()).toBe(true);
        expect(router.url).toBe('/admin');
      } finally {
        f.destroy();
      }
    },
  );

  it('maps only the known conflict code to a local message', () => {
    const message = userError(
      new HttpErrorResponse({
        status: 409,
        error: {
          code: 'last_admin',
          error: '<script>technical detail</script>',
        },
      }),
      true,
    );
    expect(message).toContain('último administrador');
    expect(message).not.toContain('script');
  });
  it.each([
    null,
    'last_admin',
    { code: 'unknown' },
    { error: 'No se puede cambiar el rol del último administrador.' },
  ])('keeps other conflict bodies on the existing safe fallback: %j', (body) => {
    const message = userError(new HttpErrorResponse({ status: 409, error: body }), true);
    expect(message).toContain('Conflicto:');
    expect(message).not.toContain('último administrador');
  });
  it('does not confuse a 403 with a last-admin conflict', () => {
    const message = userError(
      new HttpErrorResponse({ status: 403, error: { code: 'last_admin' } }),
      true,
    );
    expect(message).toContain('No tienes permiso');
    expect(message).not.toContain('último administrador');
  });
});
