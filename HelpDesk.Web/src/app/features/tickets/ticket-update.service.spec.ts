import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { TicketOperation, TicketService } from './ticket.service';
import { ticketFixture } from './ticket.testing';

describe('Ticket update contracts', () => {
  let http: HttpTestingController;
  let service: TicketService;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(TicketService);
  });
  afterEach(() => http.verify());
  it.each(['status', 'priority', 'category', 'assignment'] as TicketOperation[])(
    'sends only the %s DTO',
    (op) => {
      const data = {
        status: 'InProgress',
        priority: 'High',
        category: 'Network',
        assignedToUserId: null,
      } as const;
      const next = vi.fn();
      service.update(41, op, data).subscribe(next);
      const r = http.expectOne(`/api/tickets/41/${op}`);
      expect(r.request.method).toBe('PATCH');
      const key =
        op === 'assignment' ? 'assignedToUserId' : (op as 'status' | 'priority' | 'category');
      expect(r.request.body).toEqual({ [key]: data[key] });
      r.flush(ticketFixture());
      expect(next).toHaveBeenCalledWith(ticketFixture());
    },
  );
  it('rejects a mismatched response without retry', () => {
    const error = vi.fn();
    service.update(41, 'text', { title: 'a', description: 'b' }).subscribe({ error });
    http.expectOne('/api/tickets/41').flush(ticketFixture({ id: 42 }));
    expect(error).toHaveBeenCalled();
    http.expectNone('/api/tickets/41');
  });
});
