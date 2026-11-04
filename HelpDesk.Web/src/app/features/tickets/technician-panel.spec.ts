import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from '../../app';
import { routes } from '../../app.routes';
import { AuthService } from '../../core/auth/auth.service';
import { authInterceptor } from '../../core/auth/auth.interceptor';
import { signIn } from '../../core/auth/auth.testing';
import { pageFixture, ticketFixture } from './ticket.testing';
import { TicketService } from './ticket.service';
import { defaultTicketQuery } from './ticket-query';

describe('Technician panel with router and simulated HTTP', () => {
  let http: HttpTestingController;
  let auth: AuthService;
  let router: Router;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes),
      provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });
  afterEach(() => { auth.logout(); http.verify(); });
  const start = async () => {
    signIn(auth, http, 'Technician');
    const fixture = TestBed.createComponent(App);
    await router.navigateByUrl('/my-tickets?priority=High&page=2');
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  };
  const summary = () => {
    for (const [status, total] of [['Open', 19], ['InProgress', 7], ['Resolved', 0]] as const) {
      const request = http.expectOne(r => r.url === '/api/tickets' && r.params.get('status') === status);
      expect(request.request.params.get('assignedToMe')).toBe('true');
      expect(request.request.params.has('priority')).toBe(false);
      request.flush(pageFixture({ items: [], totalCount: total, pageSize: 1, totalPages: total }));
    }
  };
  it.each(['User', 'Admin', null] as const)('blocks %s before requesting tickets', async role => {
    if (role) signIn(auth, http, role);
    const fixture = TestBed.createComponent(App);
    await router.navigateByUrl('/my-tickets');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(router.url).toBe(role ? '/session' : '/login');
    http.expectNone(r => r.url === '/api/tickets');
    expect(fixture.nativeElement.querySelector('a[href="/my-tickets"]')).toBeNull();
  });
  it('uses server totals and preserves pagination on return from detail', async () => {
    const fixture = await start();
    summary();
    const list = http.expectOne(r => r.url === '/api/tickets');
    expect(list.request.params.get('assignedToMe')).toBe('true');
    expect(list.request.params.get('priority')).toBe('High');
    expect(list.request.params.get('page')).toBe('2');
    list.flush(pageFixture({ page: 2, totalCount: 11, totalPages: 2,
      items: [ticketFixture({ assignedToUserId: 3 })] }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.summary').textContent).toContain('19');
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Mis incidencias');
    (fixture.nativeElement.querySelector('tbody button, tbody a') as HTMLElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectOne('/api/tickets/41').flush(ticketFixture({ assignedToUserId: 3 }));
    fixture.detectChanges();
    const back = fixture.nativeElement.querySelector('.back-link') as HTMLAnchorElement;
    expect(back.getAttribute('href')).toContain('/my-tickets?priority=High&page=2');
    back.click();
    await fixture.whenStable();
    fixture.detectChanges();
    summary();
    const returned = http.expectOne(r => r.url === '/api/tickets');
    expect(returned.request.params.get('page')).toBe('2');
    returned.flush(pageFixture({ page: 2, items: [], totalCount: 0, totalPages: 0 }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No hay coincidencias');
  });
  it('combines search, status and priority without reloading the summary', async () => {
    const fixture = await start();
    summary();
    http.expectOne(r => r.url === '/api/tickets').flush(pageFixture({ page: 2 }));
    await router.navigateByUrl('/my-tickets?search=printer&status=Open&priority=High&pageSize=5');
    fixture.detectChanges();
    await fixture.whenStable();
    const request = http.expectOne(r => r.url === '/api/tickets');
    expect(request.request.params.get('assignedToMe')).toBe('true');
    expect(request.request.params.get('search')).toBe('printer');
    expect(request.request.params.get('status')).toBe('Open');
    expect(request.request.params.get('priority')).toBe('High');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(pageFixture({ items: [], totalCount: 0, totalPages: 0, pageSize: 5 }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No hay coincidencias');
    http.expectNone(r => r.url === '/api/tickets');
  });
  it.each([200, 403, 409])('reuses status management from panel detail with HTTP %s', async status => {
    signIn(auth, http, 'Technician');
    const fixture = TestBed.createComponent(App);
    await router.navigateByUrl('/tickets/41?from=my-tickets&status=Open&page=2');
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne('/api/tickets/41').flush(ticketFixture({ assignedToUserId: 3 }));
    fixture.detectChanges();
    const select = fixture.nativeElement.querySelector('#edit-status') as HTMLSelectElement;
    expect(Array.from(select.options).map(o => o.value)).toEqual(['Open', 'InProgress']);
    select.value = 'InProgress';
    select.dispatchEvent(new Event('change'));
    (fixture.nativeElement.querySelector('app-ticket-management form') as HTMLFormElement)
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
    const request = http.expectOne('/api/tickets/41/status');
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ status: 'InProgress' });
    if (status === 200) request.flush(ticketFixture({ assignedToUserId: 3, status: 'InProgress' }));
    else request.flush({}, { status, statusText: 'Test denial' });
    fixture.detectChanges();
    expect(auth.isAuthenticated()).toBe(true);
    if (status !== 200) expect(fixture.nativeElement.textContent).toContain('Actualizar detalle');
    http.expectNone('/api/tickets/41/status');
    expect(fixture.nativeElement.querySelector('.back-link').getAttribute('href'))
      .toContain('/my-tickets?status=Open&page=2');
  });
  it.each([401, 403, 500])('handles HTTP %s without retries or incorrect logout', async status => {
    const fixture = await start();
    const requests = http.match(r => r.url === '/api/tickets');
    expect(requests).toHaveLength(4);
    for (const request of requests) {
      if (!request.cancelled) request.flush({}, { status, statusText: 'Test error' });
    }
    await fixture.whenStable();
    fixture.detectChanges();
    expect(auth.isAuthenticated()).toBe(status !== 401);
    expect(router.url).toBe(status === 401 ? '/login' : '/my-tickets?priority=High&page=2');
    http.expectNone(r => r.url === '/api/tickets');
    if (status === 403) expect(fixture.nativeElement.textContent).toContain('No tienes permiso');
  });
  it('keeps assignment filtering explicit in the service', () => {
    const service = TestBed.inject(TicketService);
    service.list(defaultTicketQuery, true).subscribe();
    const own = http.expectOne(r => r.url === '/api/tickets');
    expect(own.request.params.get('assignedToMe')).toBe('true');
    own.flush(pageFixture());
    service.list(defaultTicketQuery).subscribe();
    const generic = http.expectOne(r => r.url === '/api/tickets');
    expect(generic.request.params.has('assignedToMe')).toBe(false);
    generic.flush(pageFixture());
  });
});
