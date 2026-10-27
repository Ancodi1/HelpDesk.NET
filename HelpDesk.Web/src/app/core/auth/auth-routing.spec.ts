import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from '../../app';
import { routes } from '../../app.routes';
import { AuthService } from './auth.service';
import { signIn } from './auth.testing';

describe('Private session route integration', () => {
  let http: HttpTestingController;
  let auth: AuthService;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });
  afterEach(() => {
    auth.logout();
    http.verify();
  });
  it('redirects an anonymous visit to the real login page', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/session');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(router.url).toBe('/login');
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain(
      'Conecta con tu espacio',
    );
    expect(fixture.nativeElement.querySelector('input[type="password"]')).not.toBeNull();
  });
  it('renders the session and closes it through the actual menu', async () => {
    signIn(auth, http, 'Technician');
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/session');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Espacio técnico');
    expect(fixture.nativeElement.textContent).toContain('person@example.invalid');
    fixture.nativeElement.querySelector('app-session-menu button').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(router.url).toBe('/login');
    expect(auth.user()).toBeNull();
    await router.navigateByUrl('/session');
    expect(router.url).toBe('/login');
  });
});
