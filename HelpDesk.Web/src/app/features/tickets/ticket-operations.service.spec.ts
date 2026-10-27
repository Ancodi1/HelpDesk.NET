import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { TicketService, TicketCreationResponseError } from './ticket.service';
import { ticketFixture } from './ticket.testing';
import { CreateTicketRequest } from './ticket.models';
import { AuthService } from '../../core/auth/auth.service';
import { authInterceptor } from '../../core/auth/auth.interceptor';
import { signIn } from '../../core/auth/auth.testing';

const input: CreateTicketRequest = {
  title: 'Test fixture title',
  description: '  Line one\nLine two  ',
  priority: 'High',
  category: 'Network',
};
describe('Ticket create and detail API contracts', () => {
  let service: TicketService;
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
    service = TestBed.inject(TicketService);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });
  afterEach(() => {
    auth.logout();
    http.verify();
    vi.useRealTimers();
  });
  it('sends only four writable fields with auth and preserves whitespace', () => {
    const session = signIn(auth, http);
    const next = vi.fn();
    service
      .create({
        ...input,
        createdByUserId: 999,
        status: 'Closed',
        assignedToUserId: 2,
      } as CreateTicketRequest)
      .subscribe(next);
    const request = http.expectOne('/api/tickets');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(input);
    expect(request.request.headers.get('Authorization')).toBe('Bearer ' + session.accessToken);
    expect(request.request.redirect).toBe('error');
    request.flush(ticketFixture({ createdByUserId: 3 }), {
      status: 201,
      statusText: 'Created',
      headers: { Location: '/api/tickets/41' },
    });
    expect(next).toHaveBeenCalledWith(expect.objectContaining({ id: 41 }));
  });
  it('reads only the requested detail through the authenticated GET endpoint', () => {
    const session = signIn(auth, http);
    const next = vi.fn();
    service.get(41).subscribe(next);
    const request = http.expectOne('/api/tickets/41');
    expect(request.request.method).toBe('GET');
    expect(request.request.cache).toBe('no-store');
    expect(request.request.headers.get('Authorization')).toBe('Bearer ' + session.accessToken);
    request.flush(ticketFixture());
    expect(next).toHaveBeenCalledWith(ticketFixture());
  });
  it.each([400, 401, 403, 404, 409, 500])(
    'propagates a failed creation without automatically retrying: %s',
    (status) => {
      signIn(auth, http);
      const error = vi.fn();
      service.create(input).subscribe({ error });
      http.expectOne('/api/tickets').flush({}, { status, statusText: 'Error' });
      expect(error).toHaveBeenCalledOnce();
      http.expectNone('/api/tickets');
    },
  );
  it.each([{}, null, { ...ticketFixture(), id: 0 }, { ...ticketFixture(), status: 'Unknown' }])(
    'does not guess an ID from an unreadable creation response',
    (body) => {
      const error = vi.fn();
      service.create(input).subscribe({ error });
      http
        .expectOne('/api/tickets')
        .flush(body, {
          status: 201,
          statusText: 'Created',
          headers: { Location: 'https://example.invalid/elsewhere' },
        });
      expect(error.mock.calls[0][0]).toBeInstanceOf(TicketCreationResponseError);
      http.expectNone('https://example.invalid/elsewhere');
    },
  );
  it('rejects a non-201 success response rather than treating it as confirmed creation', () => {
    const error = vi.fn();
    service.create(input).subscribe({ error });
    http.expectOne('/api/tickets').flush(ticketFixture());
    expect(error.mock.calls[0][0]).toBeInstanceOf(TicketCreationResponseError);
  });
  it('rejects a detail response for another ID', () => {
    const error = vi.fn();
    service.get(41).subscribe({ error });
    http.expectOne('/api/tickets/41').flush(ticketFixture({ id: 42 }));
    expect(error).toHaveBeenCalledOnce();
  });
  it.each([null, {}, { ...ticketFixture(), createdAt: 'invalid' }])(
    'rejects invalid detail payloads',
    (body) => {
      const error = vi.fn();
      service.get(41).subscribe({ error });
      http.expectOne('/api/tickets/41').flush(body);
      expect(error).toHaveBeenCalledOnce();
    },
  );
  it('times out creation without retrying an uncertain write', () => {
    vi.useFakeTimers();
    const error = vi.fn();
    service.create(input).subscribe({ error });
    const request = http.expectOne('/api/tickets');
    vi.advanceTimersByTime(15001);
    expect(request.cancelled).toBe(true);
    expect(error).toHaveBeenCalledOnce();
    http.expectNone('/api/tickets');
  });
});
