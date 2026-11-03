import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from '../../../app';
import { routes } from '../../../app.routes';
import { AuthService } from '../../../core/auth/auth.service';
import { authInterceptor } from '../../../core/auth/auth.interceptor';
import { loginResponse, signIn } from '../../../core/auth/auth.testing';
import { ticketFixture } from '../ticket.testing';

describe('Ticket DOM submission with real session, interceptor and routes', () => {
  let f: ComponentFixture<App>;
  let http: HttpTestingController;
  let auth: AuthService;
  let router: Router;
  let token: string;
  beforeEach(async () => {
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
    token = signIn(auth, http).accessToken;
    f = TestBed.createComponent(App);
    await visit('/tickets/41?priority=High&page=2');
    http.expectOne('/api/tickets/41').flush(ticketFixture());
    f.detectChanges();
  });
  afterEach(() => {
    f.destroy();
    auth.logout();
    http.verify();
  });
  async function visit(url: string) {
    await router.navigateByUrl(url);
    f.detectChanges();
    await f.whenStable();
  }
  function edit(selector: string, value: string, event = 'input') {
    const element = f.nativeElement.querySelector(selector) as HTMLInputElement;
    element.value = value;
    element.dispatchEvent(new Event(event, { bubbles: true }));
    f.detectChanges();
  }
  function submit(selector = 'app-ticket-management form') {
    const form = f.nativeElement.querySelector(selector) as HTMLFormElement;
    const event = new Event('submit', { bubbles: true, cancelable: true });
    form.dispatchEvent(event);
    // Angular must cancel navigation, otherwise the browser reloads and loses the in-memory JWT.
    expect(event.defaultPrevented).toBe(true);
    f.detectChanges();
  }
  it.each([
    [
      'text',
      '#edit-title',
      'Changed title',
      '/api/tickets/41',
      { title: 'Changed title', description: 'Test fixture description' },
    ],
    [
      'text',
      '#edit-description',
      'Changed description',
      '/api/tickets/41',
      { title: 'Test fixture incident', description: 'Changed description' },
    ],
    ['priority', '#edit-priority', 'High', '/api/tickets/41/priority', { priority: 'High' }],
    ['category', '#edit-category', 'Network', '/api/tickets/41/category', { category: 'Network' }],
    ['status', '#edit-status', 'InProgress', '/api/tickets/41/status', { status: 'InProgress' }],
    ['assignment', '#edit-assignment', '7', '/api/tickets/41/assignment', { assignedToUserId: 7 }],
    [
      'assignment',
      '#edit-assignment',
      '',
      '/api/tickets/41/assignment',
      { assignedToUserId: null },
    ],
  ] as const)(
    'submits %s through the form without losing the session (%s=%s)',
    async (op, selector, value, url, body) => {
      edit('#operation', op, 'change');
      edit(selector, value, op === 'text' || op === 'assignment' ? 'input' : 'change');
      submit();
      submit(); // duplicate native events must still issue only one request
      const request = http.expectOne(url);
      expect(request.request.method).toBe(op === 'text' ? 'PUT' : 'PATCH');
      expect(request.request.body).toEqual(body);
      expect(request.request.headers.get('Authorization')).toBe('Bearer ' + token);
      request.flush(ticketFixture(body));
      f.detectChanges();
      expect(auth.isAuthenticated()).toBe(true);
      expect(router.url).toBe('/tickets/41?priority=High&page=2');
      expect(f.nativeElement.textContent).toContain('Cambios guardados correctamente');
      await visit('/session');
      expect(auth.isAuthenticated()).toBe(true);
      expect(router.url).toBe('/session');
    },
  );
  it.each(['#edit-title', '#edit-description'])(
    'prevents native navigation for invalid %s without making a write',
    (selector) => {
      edit(selector, '   ');
      submit();
      http.expectNone((r) => r.method === 'PUT' || r.method === 'PATCH');
      expect(auth.isAuthenticated()).toBe(true);
      expect(router.url).toContain('/tickets/41');
    },
  );
  it.each([400, 403, 404, 409, 0])('keeps session and route on a write error %s', (status) => {
    submit();
    const request = http.expectOne('/api/tickets/41');
    if (status === 0) request.error(new ProgressEvent('error'));
    else request.flush({}, { status, statusText: 'Error' });
    f.detectChanges();
    expect(auth.isAuthenticated()).toBe(true);
    expect(router.url).toContain('/tickets/41');
    expect(f.nativeElement.textContent).toContain('Actualizar detalle');
    http.expectNone((r) => r.method === 'PUT' || r.method === 'PATCH');
  });
  it('recovers from a genuine 401 via the login form and guards without a loop', async () => {
    submit();
    http.expectOne('/api/tickets/41').flush({}, { status: 401, statusText: 'Unauthorized' });
    await f.whenStable();
    f.detectChanges();
    expect(auth.user()).toBeNull();
    expect(router.url).toBe('/login');
    // Failed login remains on login and can be retried manually.
    edit('#login-email', 'person@example.invalid');
    edit('#login-password', 'unit-test-only-value');
    submit('app-login form');
    const failed = http.expectOne('/api/auth/login');
    expect(failed.request.headers.has('Authorization')).toBe(false);
    failed.flush({}, { status: 401, statusText: 'Unauthorized' });
    f.detectChanges();
    expect(router.url).toBe('/login');
    edit('#login-password', 'unit-test-only-value');
    submit('app-login form');
    http.expectOne('/api/auth/login').flush(loginResponse());
    await f.whenStable();
    f.detectChanges();
    expect(router.url).toBe('/session');
    expect(auth.isAuthenticated()).toBe(true);
    await visit('/tickets/41');
    http.expectOne('/api/tickets/41').flush(ticketFixture());
    f.detectChanges();
    submit();
    http.expectOne('/api/tickets/41').flush(ticketFixture());
    await f.whenStable();
    f.detectChanges();
    expect(router.url).toBe('/tickets/41');
    expect(auth.isAuthenticated()).toBe(true);
  });
});
