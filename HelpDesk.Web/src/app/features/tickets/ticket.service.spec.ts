import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { TicketService } from './ticket.service';
import { defaultTicketQuery } from './ticket-query';
import { pageFixture, ticketFixture } from './ticket.testing';
import { AuthService } from '../../core/auth/auth.service';
import { authInterceptor } from '../../core/auth/auth.interceptor';
import { signIn } from '../../core/auth/auth.testing';

describe('TicketService read-only API', () => {
  let service: TicketService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    service = TestBed.inject(TicketService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    TestBed.inject(AuthService).logout();
    http.verify();
    vi.useRealTimers();
  });
  it('uses GET with only supported parameters, auth and no-store', () => {
    const session = signIn(TestBed.inject(AuthService), http);
    const next = vi.fn();
    service
      .list({
        ...defaultTicketQuery,
        search: '  %_\\ + prueba  ',
        status: 'Open',
        priority: 'High',
        category: 'Network',
        page: 2,
        pageSize: 5,
      })
      .subscribe(next);
    const request = http.expectOne((r) => r.url === '/api/tickets');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.keys().sort()).toEqual([
      'category',
      'page',
      'pageSize',
      'priority',
      'search',
      'status',
    ]);
    expect(request.request.params.get('search')).toBe('  %_\\ + prueba  ');
    expect(request.request.headers.get('Authorization')).toBe('Bearer ' + session.accessToken);
    expect(request.request.cache).toBe('no-store');
    request.flush(pageFixture({ page: 2, pageSize: 5, totalCount: 6, totalPages: 2 }));
    expect(next).toHaveBeenCalledOnce();
  });
  it('omits empty filters and keeps nullable relation IDs', () => {
    const next = vi.fn();
    service.list({ ...defaultTicketQuery }).subscribe(next);
    const request = http.expectOne((r) => r.url === '/api/tickets');
    expect(request.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    request.flush(pageFixture());
    expect(next.mock.calls[0][0].items[0].assignedToUserId).toBeNull();
  });
  it('accepts an empty page, including an out-of-range page', () => {
    const next = vi.fn();
    service.list({ ...defaultTicketQuery, page: 9 }).subscribe(next);
    http.expectOne((r) => r.url === '/api/tickets').flush(pageFixture({ page: 9, items: [] }));
    expect(next).toHaveBeenCalledOnce();
  });
  it.each([
    null,
    {},
    { items: 'invalid' },
    { totalPages: 22 },
    { page: 2 },
    { pageSize: 5 },
    { totalCount: -1 },
    { items: [ticketFixture({ status: 'Unknown' as never })] },
    { items: [ticketFixture({ createdAt: 'invalid' })] },
    { items: [{ id: 1, title: 'incomplete' }] },
  ])('rejects an invalid response without inventing rows', (patch) => {
    const error = vi.fn();
    service.list({ ...defaultTicketQuery }).subscribe({ error });
    http
      .expectOne((r) => r.url === '/api/tickets')
      .flush(
        patch === null
          ? null
          : Object.keys(patch).length === 0
            ? {}
            : { ...pageFixture(), ...patch },
      );
    expect(error).toHaveBeenCalledOnce();
  });
  it('propagates server errors to the page', () => {
    const error = vi.fn();
    service.list({ ...defaultTicketQuery }).subscribe({ error });
    http
      .expectOne((r) => r.url === '/api/tickets')
      .flush({}, { status: 403, statusText: 'Forbidden' });
    expect(error).toHaveBeenCalledOnce();
  });
  it('cancels on timeout', () => {
    vi.useFakeTimers();
    const error = vi.fn();
    service.list({ ...defaultTicketQuery }).subscribe({ error });
    const request = http.expectOne((r) => r.url === '/api/tickets');
    vi.advanceTimersByTime(15001);
    expect(request.cancelled).toBe(true);
    expect(error).toHaveBeenCalledOnce();
  });
});
